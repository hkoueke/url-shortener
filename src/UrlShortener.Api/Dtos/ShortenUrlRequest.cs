namespace UrlShortener.Api.Dtos;

/// <summary>Request payload used to create a short URL.</summary>
/// <param name="LongUrl">The original URL to shorten.</param>
/// <param name="CustomCode">Optional custom alias code.</param>
/// <param name="ExpiresAtUtc">Optional expiration date/time in UTC.</param>
/// <param name="OwnerTeam">Owning team identifier.</param>
public sealed record ShortenUrlRequest(string LongUrl, string? CustomCode, DateTime? ExpiresAtUtc, string OwnerTeam);

/// <summary>Response payload containing the generated short URL.</summary>
/// <param name="ShortUrl">The generated short URL.</param>
public sealed record ShortenUrlResponse(string ShortUrl);
