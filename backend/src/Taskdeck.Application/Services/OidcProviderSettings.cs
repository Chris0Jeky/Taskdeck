namespace Taskdeck.Application.Services;

/// <summary>
/// Configuration for a single OIDC provider (e.g., Microsoft Entra ID, Google).
/// OIDC is only active when Authority, ClientId, and ClientSecret are all configured.
/// </summary>
public class OidcProviderConfig
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = ["openid", "profile", "email"];
    public string CallbackPath { get; set; } = string.Empty;

    /// <summary>
    /// Returns true when this OIDC provider is fully configured and should be active.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(Authority)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// Top-level OIDC configuration holding all configured providers.
/// </summary>
public class OidcSettings
{
    public List<OidcProviderConfig> Providers { get; set; } = [];

    /// <summary>
    /// Returns all providers that are fully configured.
    /// </summary>
    public IReadOnlyList<OidcProviderConfig> ConfiguredProviders =>
        Providers.Where(p => p.IsConfigured).ToList().AsReadOnly();
}
