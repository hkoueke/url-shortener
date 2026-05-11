namespace UrlShortener.Api.Observability;

/// <summary>Source-generated log message definitions for high-performance structured logging.</summary>
public static partial class ShortenerLogMessages
{
    /// <summary>Logs a URL creation event.</summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="code">Generated short code.</param>
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Short URL created with code {Code}")]
    public static partial void ShortUrlCreated(ILogger logger, string code);

    /// <summary>Logs a redirect event.</summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="code">Requested short code.</param>
    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Redirect executed for code {Code}")]
    public static partial void RedirectExecuted(ILogger logger, string code);

    /// <summary>Logs a cache resolution sample.</summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="code">Requested short code.</param>
    /// <param name="hit">True when cache hit occurred.</param>
    [LoggerMessage(EventId = 1003, Level = LogLevel.Debug, Message = "Cache lookup for code {Code}, hit = {Hit}")]
    public static partial void CacheLookup(ILogger logger, string code, bool hit);
}
