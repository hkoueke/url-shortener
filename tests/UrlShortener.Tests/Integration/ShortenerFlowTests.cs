using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using UrlShortener.Api.Data;

namespace UrlShortener.Tests.Integration;

/// <summary>Validates end-to-end shorten/redirect behavior against real Postgres and Redis containers, including cache fail-safe behavior.</summary>
public sealed class ShortenerFlowTests : IAsyncLifetime
{
    private readonly IContainer _postgres = new ContainerBuilder().WithImage("postgres:16").WithEnvironment("POSTGRES_DB", "shortener").WithEnvironment("POSTGRES_USER", "app").WithEnvironment("POSTGRES_PASSWORD", "app").WithPortBinding(5432, true).WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432)).Build();
    private readonly IContainer _redis = new ContainerBuilder().WithImage("redis:7").WithPortBinding(6379, true).WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(6379)).Build();

    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();

        var postgresConn = $"Host=localhost;Port={_postgres.GetMappedPublicPort(5432)};Database=shortener;Username=app;Password=app";
        var redisConn = $"localhost:{_redis.GetMappedPublicPort(6379)}";

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", postgresConn);
            builder.UseSetting("ConnectionStrings:Redis", redisConn);
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                var scope = services.BuildServiceProvider().CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Executes auth, shorten, and redirect flow and asserts redirect works after Redis stop to prove fail-safe retrieval path.</summary>
    [Fact]
    public async Task Auth_shorten_redirect_and_failsafe_flow_should_work()
    {
        var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var request = new StringContent(JsonSerializer.Serialize(new { longUrl = "https://example.org/deep/link", ownerTeam = "platform" }), Encoding.UTF8, "application/json");
        var shortenResponse = await client.PostAsync("/api/v1/urls", request);

        shortenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await shortenResponse.Content.ReadFromJsonAsync<ShortResponse>();
        payload.Should().NotBeNull();

        var firstRedirect = await client.GetAsync(payload!.ShortUrl);
        firstRedirect.StatusCode.Should().Be(HttpStatusCode.Redirect);

        await _redis.StopAsync();

        var secondRedirect = await client.GetAsync(payload.ShortUrl);
        secondRedirect.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    private sealed record ShortResponse(string ShortUrl);

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([
                new Claim("scope", "links:write"),
                new Claim(ClaimTypes.Name, "integration-tester")
            ], "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    [Fact]
    public async Task Duplicate_custom_alias_should_return_conflict()
    {
        var client = _factory!.CreateClient();
        var body = new { longUrl = "https://example.org/a", customCode = "same-code", ownerTeam = "platform" };
        var req1 = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var req2 = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var r1 = await client.PostAsync("/api/v1/urls", req1);
        var r2 = await client.PostAsync("/api/v1/urls", req2);
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }


    [Fact]
    public async Task Timeseries_invalid_window_should_return_bad_request()
    {
        var client = _factory!.CreateClient();
        var response = await client.GetAsync("/api/v1/urls/abc/stats/timeseries?fromUtc=2026-02-01T00:00:00Z&toUtc=2026-01-01T00:00:00Z&bucket=day");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

}
