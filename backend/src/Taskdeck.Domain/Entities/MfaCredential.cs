using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Stores a TOTP-based MFA credential for a user.
/// Each user may have at most one active TOTP credential.
/// WARNING: The shared secret is currently stored as plaintext in the database.
/// A future enhancement should add an EF Core value converter with Data Protection
/// or AES-GCM encryption before this feature is used in production deployments.
/// </summary>
public class MfaCredential : Entity
{
    private string _secret = string.Empty;

    public Guid UserId { get; private set; }

    /// <summary>
    /// Base32-encoded TOTP shared secret.
    /// </summary>
    public string Secret
    {
        get => _secret;
        private set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException(ErrorCodes.ValidationError, "MFA secret cannot be empty");

            if (value.Length > 512)
                throw new DomainException(ErrorCodes.ValidationError, "MFA secret cannot exceed 512 characters");

            _secret = value;
        }
    }

    /// <summary>
    /// Whether this credential has been confirmed by the user entering a valid TOTP code.
    /// Unconfirmed credentials should not be used for authentication gates.
    /// </summary>
    public bool IsConfirmed { get; private set; }

    /// <summary>
    /// Comma-separated recovery codes (hashed). Generated at setup time.
    /// </summary>
    public string? RecoveryCodes { get; private set; }

    private MfaCredential() : base() { }

    public MfaCredential(Guid userId, string secret)
        : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");

        UserId = userId;
        Secret = secret;
        IsConfirmed = false;
    }

    /// <summary>
    /// Confirms the credential after the user successfully validates a TOTP code.
    /// </summary>
    public void Confirm()
    {
        IsConfirmed = true;
        Touch();
    }

    /// <summary>
    /// Sets the hashed recovery codes for this credential.
    /// Pass null or empty to clear all recovery codes (e.g., when exhausted).
    /// </summary>
    public void SetRecoveryCodes(string? hashedRecoveryCodes)
    {
        RecoveryCodes = string.IsNullOrWhiteSpace(hashedRecoveryCodes) ? null : hashedRecoveryCodes;
        Touch();
    }

    /// <summary>
    /// Revokes this MFA credential by marking as unconfirmed.
    /// Note: The secret is intentionally preserved for audit trail purposes;
    /// full deletion should use repository deletion instead.
    /// </summary>
    public void Revoke()
    {
        IsConfirmed = false;
        Touch();
    }
}
