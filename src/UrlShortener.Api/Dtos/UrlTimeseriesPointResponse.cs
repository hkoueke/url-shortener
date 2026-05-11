namespace UrlShortener.Api.Dtos;

/// <summary>Represents a single aggregated analytics point.</summary>
/// <param name="BucketStartUtc">Bucket start in UTC.</param>
/// <param name="RedirectCount">Redirect count within bucket.</param>
public sealed record UrlTimeseriesPointResponse(DateTime BucketStartUtc, long RedirectCount);
