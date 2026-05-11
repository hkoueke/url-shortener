namespace UrlShortener.Api.Entities;

/// <summary>Represents a redirect execution event used for analytics time-series queries.</summary>
public sealed class RedirectEvent
{
    /// <summary>Gets or sets event primary key.</summary>
    public long Id { get; set; }
    /// <summary>Gets or sets referenced short code.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Gets or sets UTC event time.</summary>
    public DateTime OccurredAtUtc { get; set; }
}
