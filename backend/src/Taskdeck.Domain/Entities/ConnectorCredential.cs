using Taskdeck.Domain.Common;
using Taskdeck.Domain.Connectors;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Stores encrypted credentials for a connector instance.
/// The EncryptedValue field contains AES-256-encrypted credential data;
/// plaintext secrets are NEVER stored.
/// </summary>
public class ConnectorCredential : Entity
{
    private const int MaxLabelLength = 100;
    private const int MaxEncryptedValueLength = 8000;

    public Guid ConnectorId { get; private set; }
    public Guid UserId { get; private set; }
    public ConnectorAuthMethod AuthMethod { get; private set; }

    private string _label = string.Empty;
    public string Label
    {
        get => _label;
        private set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException(ErrorCodes.ValidationError, "Credential label cannot be empty.");

            var trimmed = value.Trim();
            if (trimmed.Length > MaxLabelLength)
                throw new DomainException(
                    ErrorCodes.ValidationError,
                    $"Credential label cannot exceed {MaxLabelLength} characters.");

            _label = trimmed;
        }
    }

    /// <summary>
    /// AES-256 encrypted credential value. Never contains plaintext.
    /// </summary>
    public string EncryptedValue { get; private set; } = string.Empty;

    /// <summary>
    /// When the credential was last rotated.
    /// </summary>
    public DateTimeOffset? RotatedAt { get; private set; }

    /// <summary>
    /// When the credential expires, if applicable.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    private ConnectorCredential() : base() { }

    public ConnectorCredential(
        Guid connectorId,
        Guid userId,
        ConnectorAuthMethod authMethod,
        string label,
        string encryptedValue,
        DateTimeOffset? expiresAt = null)
        : base()
    {
        if (connectorId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Connector ID cannot be empty.");
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty.");
        if (string.IsNullOrWhiteSpace(encryptedValue))
            throw new DomainException(ErrorCodes.ValidationError, "Encrypted value cannot be empty.");
        if (encryptedValue.Length > MaxEncryptedValueLength)
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Encrypted value cannot exceed {MaxEncryptedValueLength} characters.");

        ConnectorId = connectorId;
        UserId = userId;
        AuthMethod = authMethod;
        Label = label;
        EncryptedValue = encryptedValue;
        ExpiresAt = expiresAt;
    }

    public void Rotate(string newEncryptedValue, DateTimeOffset? newExpiresAt = null)
    {
        if (string.IsNullOrWhiteSpace(newEncryptedValue))
            throw new DomainException(ErrorCodes.ValidationError, "Encrypted value cannot be empty.");
        if (newEncryptedValue.Length > MaxEncryptedValueLength)
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Encrypted value cannot exceed {MaxEncryptedValueLength} characters.");

        EncryptedValue = newEncryptedValue;
        ExpiresAt = newExpiresAt;
        RotatedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value <= DateTimeOffset.UtcNow;
}
