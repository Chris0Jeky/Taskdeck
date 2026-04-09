using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// A short-lived, single-use authorization code issued after OAuth callback.
/// Stored in the database to survive restarts and support multi-instance deployments.
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
    /// The user ID this code authenticates.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// The pre-serialized JWT token to return on successful exchange.
    /// </summary>
    public string Token { get; private set; } = string.Empty;

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
        Token = token;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// Returns true if this code has expired.
    /// </summary>
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;

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
