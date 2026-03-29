using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Links a user account to an external OAuth provider (e.g. GitHub).
/// </summary>
public class ExternalLogin : Entity
{
    private string _provider = string.Empty;
    private string _providerUserId = string.Empty;

    public Guid UserId { get; private set; }

    public string Provider
    {
        get => _provider;
        private set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException(ErrorCodes.ValidationError, "Provider cannot be empty");

            if (value.Length > 50)
                throw new DomainException(ErrorCodes.ValidationError, "Provider cannot exceed 50 characters");

            _provider = value;
        }
    }

    public string ProviderUserId
    {
        get => _providerUserId;
        private set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException(ErrorCodes.ValidationError, "Provider user ID cannot be empty");

            if (value.Length > 255)
                throw new DomainException(ErrorCodes.ValidationError, "Provider user ID cannot exceed 255 characters");

            _providerUserId = value;
        }
    }

    public string? ProviderDisplayName { get; private set; }
    public string? AvatarUrl { get; private set; }

    private ExternalLogin() : base() { }

    public ExternalLogin(Guid userId, string provider, string providerUserId, string? providerDisplayName = null, string? avatarUrl = null)
        : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");

        UserId = userId;
        Provider = provider;
        ProviderUserId = providerUserId;
        ProviderDisplayName = SanitizeDisplayName(providerDisplayName);
        AvatarUrl = ValidateAvatarUrl(avatarUrl);
    }

    public void UpdateProfile(string? displayName, string? avatarUrl)
    {
        ProviderDisplayName = SanitizeDisplayName(displayName);
        AvatarUrl = ValidateAvatarUrl(avatarUrl);
        Touch();
    }

    private static string? SanitizeDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        // Limit length and strip control characters
        var sanitized = displayName.Length > 255 ? displayName[..255] : displayName;
        return new string(sanitized.Where(c => !char.IsControl(c)).ToArray());
    }

    private static string? ValidateAvatarUrl(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
            return null;

        // Only accept valid https:// URLs to prevent javascript: and other protocol attacks
        if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && avatarUrl.Length <= 2048)
        {
            return avatarUrl;
        }

        // Invalid URL — discard it silently rather than failing the login
        return null;
    }
}
