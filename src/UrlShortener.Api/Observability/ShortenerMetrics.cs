using System.Diagnostics.Metrics;
namespace UrlShortener.Api.Observability;
public static class ShortenerMetrics
{
    public static readonly Meter Meter = new("shortener.metrics");
    public static readonly Counter<long> LinksCreated = Meter.CreateCounter<long>("shortener.links.created");
    public static readonly Counter<long> RedirectsExecuted = Meter.CreateCounter<long>("shortener.redirects.executed");
    public static readonly Histogram<double> CacheHitRatio = Meter.CreateHistogram<double>("shortener.cache.hit_ratio");
}
