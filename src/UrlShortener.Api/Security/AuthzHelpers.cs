using System.Security.Claims;

namespace UrlShortener.Api.Security;

/// <summary>Provides consistent auth/scope/claim helper methods for controllers and policies.</summary>
public static class AuthzHelpers
{
    /// <summary>Returns true if the principal contains the expected OAuth scope in either scope or scp claims.</summary>
    public static bool HasScope(ClaimsPrincipal user, string expected)
    {
        var scopeClaim = user.FindFirst("scope")?.Value ?? user.FindFirst("scp")?.Value ?? string.Empty;
        var scopes = scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return scopes.Contains(expected, StringComparer.Ordinal);
    }

    /// <summary>Extracts caller team from canonical claim names.</summary>
    public static string? GetActorTeam(ClaimsPrincipal user)
        => user.FindFirst("team")?.Value ?? user.FindFirst("tenant")?.Value ?? user.FindFirst("group")?.Value;
}
