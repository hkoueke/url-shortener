using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Entities;

namespace UrlShortener.Api.Data;

/// <summary>Database context for URL shortener persistence.</summary>
public sealed class AppDbContext : DbContext
{
    /// <summary>Initializes a new <see cref="AppDbContext"/>.</summary>
    /// <param name="options">EF Core options.</param>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    /// <summary>Gets the shortened URL set.</summary>
    public DbSet<ShortenedUrl> ShortenedUrls => Set<ShortenedUrl>();
    /// <summary>Gets redirect analytics events.</summary>
    public DbSet<RedirectEvent> RedirectEvents => Set<RedirectEvent>();
    /// <summary>Gets lifecycle audit events.</summary>
    public DbSet<UrlAuditEvent> UrlAuditEvents => Set<UrlAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortenedUrl>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<RedirectEvent>().HasIndex(x => new { x.Code, x.OccurredAtUtc });
        modelBuilder.Entity<UrlAuditEvent>().HasIndex(x => new { x.Code, x.OccurredAtUtc });
    }
}
