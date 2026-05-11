using FluentAssertions;
using Microsoft.Extensions.Configuration;
using UrlShortener.Api.Core;

namespace UrlShortener.Tests.Unit;
public class SnowflakeGeneratorTests
{
    [Fact]
    public void Should_not_collide_between_workers()
    {
        var ids = new HashSet<long>();
        for (var worker = 1; worker <= 3; worker++)
        {
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Snowflake:WorkerId"] = worker.ToString() }).Build();
            var g = new SnowflakeGenerator(cfg, TimeProvider.System);
            for (var i = 0; i < 2000; i++) ids.Add(g.NextId());
        }
        ids.Count.Should().Be(6000);
    }
}
