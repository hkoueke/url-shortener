namespace UrlShortener.Api.Security;

/// <summary>Represents strongly typed Keycloak configuration used by JWT authentication.</summary>
public sealed class KeycloakOptions
{
    /// <summary>Gets or sets the Keycloak server URL (for example, https://sso.example.org).</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the Keycloak realm that issues access tokens.</summary>
    public string Realm { get; set; } = string.Empty;

    /// <summary>Gets or sets the expected API audience/client identifier.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Gets or sets whether HTTPS metadata is required while downloading OIDC configuration.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;
}
