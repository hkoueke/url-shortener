namespace UrlShortener.Api.Dtos;

/// <summary>Request payload to update link lifecycle fields.</summary>
/// <param name="IsDisabled">Optional disable flag.</param>
/// <param name="ExpiresAtUtc">Optional new expiration date in UTC.</param>
public sealed record UpdateUrlRequest(bool? IsDisabled, DateTime? ExpiresAtUtc);
