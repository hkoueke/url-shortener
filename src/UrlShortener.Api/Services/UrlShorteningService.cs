using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Core;
using UrlShortener.Api.Data;
using UrlShortener.Api.Dtos;
using UrlShortener.Api.Entities;
using UrlShortener.Api.Observability;
using UrlShortener.Api.Streaming;
using ZiggyCreatures.Caching.Fusion;

namespace UrlShortener.Api.Services;

public interface IUrlShorteningService
{
    Task<string> ShortenAsync(ShortenUrlRequest request, string actor);
    Task<string?> ResolveAsync(string code);
    Task<UrlStatsResponse?> GetStatsAsync(string code, string? actorTeam, bool isAdmin);
    Task<bool> UpdateAsync(string code, UpdateUrlRequest request, string actor, string? actorTeam, bool isAdmin);
    Task<IReadOnlyList<UrlStatsResponse>> GetTopAsync(int take, string? actorTeam, bool isAdmin);
    Task<IReadOnlyList<UrlTimeseriesPointResponse>> GetTimeseriesAsync(string code, DateTime fromUtc, DateTime toUtc, string bucket, string? actorTeam, bool isAdmin);
}

public sealed class UrlShorteningService(AppDbContext dbContext, IFusionCache cache, ISnowflakeGenerator snowflakeGenerator, ILogger<UrlShorteningService> logger, IConfiguration configuration, IAuditEventPublisher eventPublisher) : IUrlShorteningService
{
    public async Task<string> ShortenAsync(ShortenUrlRequest request, string actor)
    {
        var uri = new Uri(request.LongUrl);
        var allowedHostsByTeam = configuration.GetSection("Shortener:AllowedHostsByTeam").GetChildren().ToDictionary(x => x.Key, x => x.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var allowedForTeam = allowedHostsByTeam.TryGetValue(request.OwnerTeam, out var hosts) ? hosts : configuration["Shortener:AllowedHosts"] ?? string.Empty;
        var allowedHosts = allowedForTeam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (allowedHosts.Length > 0 && !allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Destination host is not permitted by policy.");

        var code = string.IsNullOrWhiteSpace(request.CustomCode) ? Base62Converter.Encode(snowflakeGenerator.NextId()) : request.CustomCode.Trim();
        if (code.Length is < 4 or > 32 || !code.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')) throw new InvalidOperationException("Custom code must be 4-32 characters and alphanumeric plus '-' or '_'.");
        if (await dbContext.ShortenedUrls.AnyAsync(x => x.Code == code)) throw new DuplicateNameException("Short code already exists.");

        dbContext.ShortenedUrls.Add(new ShortenedUrl { Id = snowflakeGenerator.NextId(), Code = code, LongUrl = request.LongUrl, CreatedAtUtc = DateTime.UtcNow, RedirectCount = 0, ExpiresAtUtc = request.ExpiresAtUtc, OwnerTeam = request.OwnerTeam, IsDisabled = false });
        dbContext.UrlAuditEvents.Add(new UrlAuditEvent { Id = snowflakeGenerator.NextId(), Code = code, Action = "created", Actor = actor, OccurredAtUtc = DateTime.UtcNow });
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new DuplicateNameException("Short code already exists.");
        }

        ShortenerMetrics.LinksCreated.Add(1);
        await eventPublisher.PublishAsync("url.created", new { code, request.OwnerTeam, actor, request.LongUrl });
        ShortenerLogMessages.ShortUrlCreated(logger, code);
        return code;
    }

    public async Task<string?> ResolveAsync(string code)
    {
        var hit = true;
        var item = await cache.GetOrSetAsync($"url:{code}", async _ => { hit = false; return await dbContext.ShortenedUrls.Where(x => x.Code == code).SingleOrDefaultAsync(); });
        ShortenerMetrics.CacheHitRatio.Record(hit ? 1 : 0);
        ShortenerLogMessages.CacheLookup(logger, code, hit);
        if (item is null || item.IsDisabled || (item.ExpiresAtUtc is not null && item.ExpiresAtUtc <= DateTime.UtcNow)) return null;

        await dbContext.ShortenedUrls.Where(x => x.Code == code).ExecuteUpdateAsync(s => s.SetProperty(x => x.RedirectCount, x => x.RedirectCount + 1));
        dbContext.RedirectEvents.Add(new RedirectEvent { Id = snowflakeGenerator.NextId(), Code = code, OccurredAtUtc = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();
        await eventPublisher.PublishAsync("url.redirected", new { code, occurredAtUtc = DateTime.UtcNow });
        return item.LongUrl;
    }

    public async Task<UrlStatsResponse?> GetStatsAsync(string code, string? actorTeam, bool isAdmin)
    {
        var row = await dbContext.ShortenedUrls.Where(x => x.Code == code).SingleOrDefaultAsync();
        if (row is null) return null;
        if (!isAdmin && !string.Equals(row.OwnerTeam, actorTeam, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Link ownership policy denied.");
        return new UrlStatsResponse(row.Code, row.LongUrl, row.RedirectCount, row.CreatedAtUtc);
    }

    public async Task<bool> UpdateAsync(string code, UpdateUrlRequest request, string actor, string? actorTeam, bool isAdmin)
    {
        var row = await dbContext.ShortenedUrls.SingleOrDefaultAsync(x => x.Code == code);
        if (row is null) return false;
        if (!isAdmin && !string.Equals(row.OwnerTeam, actorTeam, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Link ownership policy denied.");

        row.IsDisabled = request.IsDisabled ?? row.IsDisabled;
        row.ExpiresAtUtc = request.ExpiresAtUtc ?? row.ExpiresAtUtc;
        dbContext.UrlAuditEvents.Add(new UrlAuditEvent { Id = snowflakeGenerator.NextId(), Code = code, Action = "updated", Actor = actor, OccurredAtUtc = DateTime.UtcNow });
        await dbContext.SaveChangesAsync();
        await eventPublisher.PublishAsync("url.updated", new { code, actor, row.IsDisabled, row.ExpiresAtUtc });
        return true;
    }

    public async Task<IReadOnlyList<UrlStatsResponse>> GetTopAsync(int take, string? actorTeam, bool isAdmin)
    {
        var query = dbContext.ShortenedUrls.AsQueryable();
        if (!isAdmin && !string.IsNullOrWhiteSpace(actorTeam)) query = query.Where(x => x.OwnerTeam == actorTeam);
        return await query.OrderByDescending(x => x.RedirectCount).Take(take).Select(x => new UrlStatsResponse(x.Code, x.LongUrl, x.RedirectCount, x.CreatedAtUtc)).ToListAsync();
    }

    public async Task<IReadOnlyList<UrlTimeseriesPointResponse>> GetTimeseriesAsync(string code, DateTime fromUtc, DateTime toUtc, string bucket, string? actorTeam, bool isAdmin)
    {
        var row = await dbContext.ShortenedUrls.SingleOrDefaultAsync(x => x.Code == code);
        if (row is null) return [];
        if (!isAdmin && !string.Equals(row.OwnerTeam, actorTeam, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Link ownership policy denied.");

        var events = dbContext.RedirectEvents.Where(x => x.Code == code && x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc);
        if (bucket.Equals("hour", StringComparison.OrdinalIgnoreCase))
        {
            return await events.GroupBy(x => new DateTime(x.OccurredAtUtc.Year, x.OccurredAtUtc.Month, x.OccurredAtUtc.Day, x.OccurredAtUtc.Hour, 0, 0, DateTimeKind.Utc))
                .Select(g => new UrlTimeseriesPointResponse(g.Key, g.LongCount())).OrderBy(x => x.BucketStartUtc).ToListAsync();
        }

        return await events.GroupBy(x => x.OccurredAtUtc.Date)
            .Select(g => new UrlTimeseriesPointResponse(DateTime.SpecifyKind(g.Key, DateTimeKind.Utc), g.LongCount())).OrderBy(x => x.BucketStartUtc).ToListAsync();
    }
}
