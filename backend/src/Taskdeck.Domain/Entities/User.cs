using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Represents a user in the system with authentication and authorization information.
/// </summary>
public class User : Entity
{
    private string _username = string.Empty;
    private string _email = string.Empty;
    private string _passwordHash = string.Empty;

    public string Username
    {
        get => _username;
        private set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException(ErrorCodes.ValidationError, "Username cannot be empty");

            if (value.Length < 3)
                throw new DomainException(ErrorCodes.ValidationError, "Username must be at least 3 characters");

            if (value.Length > 50)
                throw new DomainException(ErrorCodes.ValidationError, "Username cannot exceed 50 characters");

            _username = value;
        }
    }

    public string Email
    {
        get => _email;
        private set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException(ErrorCodes.ValidationError, "Email cannot be empty");

            if (!value.Contains('@'))
                throw new DomainException(ErrorCodes.ValidationError, "Email must be valid");

            if (value.Length > 255)
                throw new DomainException(ErrorCodes.ValidationError, "Email cannot exceed 255 characters");

            _email = value.ToLowerInvariant();
        }
    }

    public string PasswordHash
    {
        get => _passwordHash;
        private set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException(ErrorCodes.ValidationError, "Password hash cannot be empty");

            _passwordHash = value;
        }
    }

    public UserRole DefaultRole { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>
    /// When set, any JWT issued before this timestamp is considered invalid.
    /// Used to invalidate active sessions after account deletion/deactivation.
    /// </summary>
    public DateTimeOffset? TokenInvalidatedAt { get; private set; }

    /// <summary>
    /// Whether this user has MFA enabled (confirmed TOTP credential exists).
    /// </summary>
    public bool MfaEnabled { get; private set; }

    private User() : base() { }

    public User(string username, string email, string passwordHash, UserRole defaultRole = UserRole.Editor)
        : base()
    {
        if (!Enum.IsDefined(defaultRole))
            throw new DomainException(ErrorCodes.ValidationError, "Default role value is invalid");

        Username = username;
        Email = email;
        PasswordHash = passwordHash;
        DefaultRole = defaultRole;
        IsActive = true;
    }

    public void UpdateProfile(string? username = null, string? email = null)
    {
        if (username != null)
            Username = username;

        if (email != null)
            Email = email;

        Touch();
    }

    public void UpdatePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            throw new DomainException(ErrorCodes.ValidationError, "Password hash cannot be empty");

        PasswordHash = newPasswordHash;
        Touch();
    }

    public void UpdateDefaultRole(UserRole role)
    {
        if (!Enum.IsDefined(role))
            throw new DomainException(ErrorCodes.ValidationError, "Default role value is invalid");

        DefaultRole = role;
        Touch();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        Touch();
    }

    public void EnableMfa()
    {
        MfaEnabled = true;
        Touch();
    }

    public void DisableMfa()
    {
        MfaEnabled = false;
        Touch();
    }

    /// <summary>
    /// Marks all existing JWT tokens as invalid by recording the current UTC time
    /// truncated to whole-second precision.  JWT <c>iat</c> claims are Unix
    /// timestamps (integer seconds), so the invalidation cutoff must use the same
    /// granularity to avoid rejecting tokens issued in the same second but after
    /// the invalidation event.
    /// </summary>
    public void InvalidateTokens()
    {
        // Truncate to second precision to match JWT iat granularity.
        TokenInvalidatedAt = DateTimeOffset.FromUnixTimeSeconds(
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Touch();
    }
}
