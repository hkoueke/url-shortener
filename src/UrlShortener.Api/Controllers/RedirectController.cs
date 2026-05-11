using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.Observability;
using UrlShortener.Api.Services;

namespace UrlShortener.Api.Controllers;

/// <summary>Provides API-version-neutral high-speed redirect execution from short code to original URL.</summary>
[ApiController]
[ApiVersionNeutral]
[Route("{code}")]
public sealed class RedirectController(IUrlShorteningService service, ILogger<RedirectController> logger) : ControllerBase
{
    /// <summary>Resolves a short code and redirects to the long URL destination.</summary>
    /// <param name="code">Short code route value.</param>
    /// <returns>An HTTP redirect or 404 if unknown.</returns>
    [HttpGet]
    public async Task<IActionResult> RedirectToLongUrl([FromRoute] string code)
    {
        var longUrl = await service.ResolveAsync(code);
        if (longUrl is null) return NotFound();

        ShortenerMetrics.RedirectsExecuted.Add(1);
        ShortenerLogMessages.RedirectExecuted(logger, code);
        return Redirect(longUrl);
    }
}
