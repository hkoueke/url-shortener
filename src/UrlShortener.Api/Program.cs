using System.Threading.RateLimiting;
using Asp.Versioning;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Prometheus;
using Scalar.AspNetCore;
using Serilog;
using StackExchange.Redis;
using UrlShortener.Api.Core;
using UrlShortener.Api.Data;
using UrlShortener.Api.Security;
using UrlShortener.Api.Streaming;
using UrlShortener.Api.Services;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
builder.Host.UseSerilog();

builder.Services.Configure<KeycloakOptions>(builder.Configuration.GetSection("Keycloak"));
builder.Services.Configure<StreamingOptions>(builder.Configuration.GetSection("Streaming"));
var keycloak = builder.Configuration.GetSection("Keycloak").Get<KeycloakOptions>() ?? new KeycloakOptions();
var authority = $"{keycloak.ServerUrl.TrimEnd('/')}/realms/{keycloak.Realm}";

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var team = AuthzHelpers.GetActorTeam(context.User) ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(team, _ => new FixedWindowRateLimiterOptions { PermitLimit = team.Equals("anonymous", StringComparison.OrdinalIgnoreCase) ? 60 : 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });
});

builder.Services.AddControllers();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) => { document.Info.Title = "Internal URL Shortener Service"; document.Info.Version = "v1"; return Task.CompletedTask; }));
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
});

builder.Services.AddCors(options => options.AddPolicy("InternalOnly", policy => policy.WithOrigins("https://intranet.example.org", "https://portal.example.org").AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.Audience = keycloak.Audience;
        options.RequireHttpsMetadata = keycloak.RequireHttpsMetadata;
        options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidIssuer = authority, ValidateAudience = true, ValidAudience = keycloak.Audience, ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30), NameClaimType = "preferred_username", RoleClaimType = "roles" };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("links:write", p => p.RequireAssertion(c => AuthzHelpers.HasScope(c.User, "links:write")));
    options.AddPolicy("links:read", p => p.RequireAssertion(c => AuthzHelpers.HasScope(c.User, "links:read") || AuthzHelpers.HasScope(c.User, "links:write")));
    options.AddPolicy("links:admin", p => p.RequireAssertion(c => AuthzHelpers.HasScope(c.User, "links:admin")));
});

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")).UseSnakeCaseNamingConvention());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISnowflakeGenerator, SnowflakeGenerator>();
builder.Services.AddScoped<IUrlShorteningService, UrlShorteningService>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<AuditEventConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        var streaming = builder.Configuration.GetSection("Streaming").Get<StreamingOptions>() ?? new StreamingOptions();
        if (streaming.Provider.Equals("RabbitMq", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(streaming.RabbitMqHost))
        {
            cfg.Host(streaming.RabbitMqHost, streaming.RabbitMqVirtualHost, h =>
            {
                if (!string.IsNullOrWhiteSpace(streaming.RabbitMqUsername)) h.Username(streaming.RabbitMqUsername);
                if (!string.IsNullOrWhiteSpace(streaming.RabbitMqPassword)) h.Password(streaming.RabbitMqPassword);
            });
        }
        else
        {
            cfg.Host("localhost", "/", _ => { });
        }
        cfg.ConfigureEndpoints(context);
    });
});
builder.Services.AddScoped<IAuditEventPublisher, MassTransitAuditEventPublisher>();

var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
var multiplexer = ConnectionMultiplexer.Connect(redisConnectionString);
builder.Services.AddSingleton<IConnectionMultiplexer>(multiplexer);
builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redisConnectionString);

builder.Services.AddFusionCache().WithDefaultEntryOptions(new FusionCacheEntryOptions { IsFailSafeEnabled = true, FactorySoftTimeout = TimeSpan.FromMilliseconds(100) }).WithDistributedCache().WithBackplane(new RedisBackplane(new RedisBackplaneOptions { Configuration = redisConnectionString }));

builder.Services.AddOpenTelemetry().WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddEntityFrameworkCoreInstrumentation().AddRedisInstrumentation().AddNpgsql()).WithMetrics(m => m.AddMeter("shortener.metrics").AddAspNetCoreInstrumentation()).UseOtlpExporter();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseCors("InternalOnly");
app.UseHttpMetrics();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapMetrics();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapControllers();

app.Run();

public partial class Program { }
