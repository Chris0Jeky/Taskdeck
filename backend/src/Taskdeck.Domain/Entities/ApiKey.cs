using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Represents an API key for MCP HTTP transport authentication.
/// Keys use the <c>tdsk_</c> prefix, are SHA-256 hashed at rest, and are bound
/// to a single user for claims-first identity mapping.
/// </summary>
public class ApiKey : Entity
{
    public const string KeyPrefix = "tdsk_";
    public const int RawKeyLength = 41; // "tdsk_" (5) + 36 base62 chars

    private string _name = string.Empty;

    /// <summary>The user this key authenticates as.</summary>
    public Guid UserId { get; private set; }

    /// <summary>SHA-256 hash of the full key (never store plaintext).</summary>
    public string KeyHash { get; private set; } = string.Empty;

    /// <summary>First 8 characters of the key for display/identification (e.g. "tdsk_a1b2").</summary>
    public string KeyPrefix_ { get; private set; } = string.Empty;

    /// <summary>User-provided name for this key (e.g. "Claude Code laptop").</summary>
    public string Name
    {
        get => _name;
        private set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException(ErrorCodes.ValidationError, "API key name cannot be empty");

            if (value.Length > 100)
                throw new DomainException(ErrorCodes.ValidationError, "API key name cannot exceed 100 characters");

            _name = value;
        }
    }

    /// <summary>Optional expiration timestamp. Null means no expiration.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Set when the key is revoked. A revoked key cannot be used.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Timestamp of the last successful authentication using this key.</summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>Whether this key is currently usable (not revoked and not expired).</summary>
    public bool IsActive => RevokedAt is null && (ExpiresAt is null || ExpiresAt > DateTimeOffset.UtcNow);

    private ApiKey() : base() { }

    public ApiKey(Guid userId, string keyHash, string keyPrefixChars, string name, DateTimeOffset? expiresAt = null)
        : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "UserId cannot be empty");

        if (string.IsNullOrWhiteSpace(keyHash))
            throw new DomainException(ErrorCodes.ValidationError, "Key hash cannot be empty");

        if (string.IsNullOrWhiteSpace(keyPrefixChars))
            throw new DomainException(ErrorCodes.ValidationError, "Key prefix cannot be empty");

        if (expiresAt.HasValue && expiresAt.Value <= DateTimeOffset.UtcNow)
            throw new DomainException(ErrorCodes.ValidationError, "Expiration must be in the future");

        UserId = userId;
        KeyHash = keyHash;
        KeyPrefix_ = keyPrefixChars;
        Name = name;
        ExpiresAt = expiresAt;
    }

    /// <summary>Revoke this key so it can no longer be used for authentication.</summary>
    public void Revoke()
    {
        if (RevokedAt is not null)
            throw new DomainException(ErrorCodes.InvalidOperation, "API key is already revoked");

        RevokedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    /// <summary>Record that this key was used for a successful authentication.</summary>
    public void RecordUsage()
    {
        LastUsedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
