using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// A one-time registration invite. Only the SHA-256 hash and a short display
/// prefix are persisted; the plaintext code is returned once by the CLI.
/// </summary>
public sealed class RegistrationInvite : Entity
{
    public const string CodePrefix = "tdi_";
    public const int RandomPartLength = 36;
    public const int RawCodeLength = 40;

    public string CodeHash { get; private set; } = string.Empty;
    public string DisplayPrefix { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public bool IsConsumed => ConsumedAt.HasValue;

    private RegistrationInvite() : base()
    {
    }

    public RegistrationInvite(string codeHash, string displayPrefix, DateTimeOffset expiresAt)
        : base()
    {
        if (string.IsNullOrWhiteSpace(codeHash) || codeHash.Length != 64)
            throw new DomainException(ErrorCodes.ValidationError, "Registration invite hash must be a SHA-256 hex value");

        if (string.IsNullOrWhiteSpace(displayPrefix) || displayPrefix.Length > 12)
            throw new DomainException(ErrorCodes.ValidationError, "Registration invite display prefix is invalid");

        if (expiresAt <= DateTimeOffset.UtcNow)
            throw new DomainException(ErrorCodes.ValidationError, "Registration invite expiration must be in the future");

        CodeHash = codeHash;
        DisplayPrefix = displayPrefix;
        ExpiresAt = expiresAt;
    }
}
