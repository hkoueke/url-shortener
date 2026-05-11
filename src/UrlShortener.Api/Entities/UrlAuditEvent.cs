namespace UrlShortener.Api.Entities;

/// <summary>Represents an audit entry for URL lifecycle actions.</summary>
public sealed class UrlAuditEvent
{
    /// <summary>Gets or sets event primary key.</summary>
    public long Id { get; set; }
    /// <summary>Gets or sets short code.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Gets or sets action name.</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Gets or sets actor identity.</summary>
    public string Actor { get; set; } = string.Empty;
    /// <summary>Gets or sets UTC event time.</summary>
    public DateTime OccurredAtUtc { get; set; }
}
