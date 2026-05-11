using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.Dtos;
using UrlShortener.Api.Services;
using UrlShortener.Api.Security;
using ZiggyCreatures.Caching.Fusion;

namespace UrlShortener.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/urls")]
public sealed class UrlsController(IUrlShorteningService service, IFusionCache cache) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "links:write")]
    public async Task<ActionResult<ShortenUrlResponse>> Shorten([FromBody] ShortenUrlRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var cached = await cache.GetOrDefaultAsync<string>($"idem:{idempotencyKey}");
            if (!string.IsNullOrWhiteSpace(cached)) return Ok(new ShortenUrlResponse(cached));
        }

        try
        {
            var actor = User.Identity?.Name ?? "unknown";
            var code = await service.ShortenAsync(request, actor);
            var shortUrl = $"/{code}";
            if (!string.IsNullOrWhiteSpace(idempotencyKey)) await cache.SetAsync($"idem:{idempotencyKey}", shortUrl, TimeSpan.FromHours(24));
            return Ok(new ShortenUrlResponse(shortUrl));
        }
        catch (DuplicateNameException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpGet("{code}/stats")]
    [Authorize(Policy = "links:read")]
    public async Task<ActionResult<UrlStatsResponse>> GetStats([FromRoute] string code)
    {
        try
        {
            var actorTeam = AuthzHelpers.GetActorTeam(User);
            var isAdmin = AuthzHelpers.HasScope(User, "links:admin");
            var stats = await service.GetStatsAsync(code, actorTeam, isAdmin);
            return stats is null ? NotFound() : Ok(stats);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("{code}/stats/timeseries")]
    [Authorize(Policy = "links:read")]
    public async Task<ActionResult<IReadOnlyList<UrlTimeseriesPointResponse>>> GetTimeseries([FromRoute] string code, [FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, [FromQuery] string bucket = "day")
    {
        if (fromUtc >= toUtc) return BadRequest(new { error = "fromUtc must be less than toUtc" });
        if (!string.Equals(bucket, "day", StringComparison.OrdinalIgnoreCase) && !string.Equals(bucket, "hour", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { error = "bucket must be 'day' or 'hour'" });
        if (toUtc - fromUtc > TimeSpan.FromDays(90)) return BadRequest(new { error = "max window is 90 days" });

        try
        {
            var actorTeam = AuthzHelpers.GetActorTeam(User);
            var isAdmin = AuthzHelpers.HasScope(User, "links:admin");
            return Ok(await service.GetTimeseriesAsync(code, fromUtc, toUtc, bucket, actorTeam, isAdmin));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpGet("top")]
    [Authorize(Policy = "links:read")]
    public async Task<ActionResult<IReadOnlyList<UrlStatsResponse>>> GetTop([FromQuery] int take = 20)
    {
        var actorTeam = AuthzHelpers.GetActorTeam(User);
        var isAdmin = AuthzHelpers.HasScope(User, "links:admin");
        return Ok(await service.GetTopAsync(Math.Clamp(take, 1, 100), actorTeam, isAdmin));
    }

    [HttpPatch("{code}")]
    [Authorize(Policy = "links:admin")]
    public async Task<IActionResult> Update([FromRoute] string code, [FromBody] UpdateUrlRequest request)
    {
        try
        {
            var actor = User.Identity?.Name ?? "unknown";
            var actorTeam = AuthzHelpers.GetActorTeam(User);
            var isAdmin = AuthzHelpers.HasScope(User, "links:admin");
            return await service.UpdateAsync(code, request, actor, actorTeam, isAdmin) ? NoContent() : NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
