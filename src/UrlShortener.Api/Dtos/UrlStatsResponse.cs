namespace UrlShortener.Api.Dtos;

/// <summary>Response payload containing analytics for a shortened URL.</summary>
/// <param name="Code">Short code.</param>
/// <param name="LongUrl">Original URL.</param>
/// <param name="RedirectCount">Total redirect count.</param>
/// <param name="CreatedAtUtc">UTC creation time.</param>
public sealed record UrlStatsResponse(string Code, string LongUrl, long RedirectCount, DateTime CreatedAtUtc);
