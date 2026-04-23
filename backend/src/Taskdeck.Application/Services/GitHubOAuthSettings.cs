using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

/// <summary>
/// Configuration for GitHub OAuth authentication.
/// OAuth is only active when both ClientId and ClientSecret are configured.
/// </summary>
public class GitHubOAuthSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Scopes that must be present in the GitHub OAuth response.
    /// Authentication is rejected if any required scope is missing.
    /// Default: "user:email" (needed to retrieve the user's email address).
    /// </summary>
    public List<string> RequiredScopes { get; set; } = new() { "user:email" };

    /// <summary>
    /// Scopes that are expected but not mandatory.
    /// A warning is logged if any expected scope is missing, but authentication proceeds.
    /// Default: "read:user", "user:email".
    /// </summary>
    public List<string> ExpectedScopes { get; set; } = new() { "read:user", "user:email" };

    /// <summary>
    /// Returns true when GitHub OAuth is fully configured and should be active.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
