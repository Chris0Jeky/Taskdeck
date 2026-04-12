using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// A short-lived, single-use authorization code issued after OAuth callback.
/// Stored in the database to survive restarts and support multi-instance deployments.
/// Supports both login (Token populated) and account-linking (ProviderData populated) flows.
/// </summary>
public class OAuthAuthCode : Entity
{
    private string _code = string.Empty;

    public string Code
    {
        get => _code;
        private set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException(ErrorCodes.ValidationError, "Auth code cannot be empty");

            if (value.Length > 512)
                throw new DomainException(ErrorCodes.ValidationError, "Auth code cannot exceed 512 characters");

            _code = value;
        }
    }

    /// <summary>
    /// The user ID this code authenticates (login flow) or Guid.Empty (link flow).
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Legacy field kept for schema compatibility. No longer populated with real JWTs --
    /// tokens are re-issued at exchange time from the stored UserId.
    /// </summary>
    public string Token { get; private set; } = string.Empty;

    /// <summary>
    /// The purpose of this code: "login" or "link".
    /// </summary>
    public string Purpose { get; private set; } = "login";

    /// <summary>
    /// JSON-serialized provider identity data for account linking flows.
    /// Contains provider, providerUserId, displayName, avatarUrl.
    /// </summary>
    public string? ProviderData { get; private set; }

    /// <summary>
    /// When this code expires and can no longer be exchanged.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// Whether this code has been consumed (exchanged for a token).
    /// </summary>
    public bool IsConsumed { get; private set; }

    /// <summary>
    /// When the code was consumed, if applicable.
    /// </summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    private OAuthAuthCode() : base() { }

    /// <summary>
    /// Creates an auth code for the login flow (token exchange).
    /// Token parameter is accepted for backward compatibility but is no longer stored;
    /// JWTs are re-issued at exchange time from the UserId.
    /// </summary>
    public OAuthAuthCode(string code, Guid userId, string token, DateTimeOffset expiresAt)
        : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (string.IsNullOrWhiteSpace(token))
            throw new DomainException(ErrorCodes.ValidationError, "Token cannot be empty");

        if (expiresAt <= DateTimeOffset.UtcNow)
            throw new DomainException(ErrorCodes.ValidationError, "Expiry must be in the future");

        Code = code;
        UserId = userId;
        Token = string.Empty; // Never store actual JWT in DB — re-issue at exchange time
        Purpose = "login";
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Creates an auth code for the account linking flow (provider identity exchange).
    /// The initiatingUserId binds this code to the user who started the link flow,
    /// preventing CSRF attacks where an attacker's GitHub is linked to a victim's account.
    /// </summary>
    public static OAuthAuthCode CreateForLinking(string code, Guid initiatingUserId, string providerData, DateTimeOffset expiresAt)
    {
        if (initiatingUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Initiating user ID is required for linking");

        if (string.IsNullOrWhiteSpace(providerData))
            throw new DomainException(ErrorCodes.ValidationError, "Provider data cannot be empty for linking");

        if (expiresAt <= DateTimeOffset.UtcNow)
            throw new DomainException(ErrorCodes.ValidationError, "Expiry must be in the future");

        var entity = new OAuthAuthCode
        {
            Code = code,
            UserId = initiatingUserId,
            Token = string.Empty,
            Purpose = "link",
            ProviderData = providerData,
            ExpiresAt = expiresAt
        };
        return entity;
    }

    /// <summary>
    /// Returns true if this code has expired.
    /// </summary>
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;

    /// <summary>
    /// Returns true if this is a linking code (not a login code).
    /// </summary>
    public bool IsLinkingCode => Purpose == "link";

    /// <summary>
    /// Attempts to consume this code. Returns false if already consumed or expired.
    /// </summary>
    public bool TryConsume()
    {
        if (IsConsumed || IsExpired)
            return false;

        IsConsumed = true;
        ConsumedAt = DateTimeOffset.UtcNow;
        Touch();
        return true;
    }
}
