namespace UrlShortener.Api.Entities;

/// <summary>Represents a persisted shortened URL mapping.</summary>
public sealed class ShortenedUrl
{
    /// <summary>Gets or sets the primary key.</summary>
    public long Id { get; set; }
    /// <summary>Gets or sets the short code.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Gets or sets the original URL.</summary>
    public string LongUrl { get; set; } = string.Empty;
    /// <summary>Gets or sets creation UTC time.</summary>
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Gets or sets the total successful redirects executed for this short URL.</summary>
    public long RedirectCount { get; set; }
    /// <summary>Gets or sets optional expiration time in UTC.</summary>
    public DateTime? ExpiresAtUtc { get; set; }
    /// <summary>Gets or sets whether this short URL is disabled.</summary>
    public bool IsDisabled { get; set; }
    /// <summary>Gets or sets owning team identifier.</summary>
    public string OwnerTeam { get; set; } = string.Empty;
}
