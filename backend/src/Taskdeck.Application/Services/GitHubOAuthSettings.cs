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
    /// Returns true when GitHub OAuth is fully configured and should be active.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
