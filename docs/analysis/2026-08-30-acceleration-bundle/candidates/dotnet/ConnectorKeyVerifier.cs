using System.Security.Cryptography;

namespace Taskdeck.Acceleration.Candidates.Operations;

public enum ConnectorKeyVerificationCode
{
    Success,
    NoCredentials,
    MissingKey,
    InvalidKey,
    CorruptCiphertext,
    StorageFailure,
    UnexpectedFailure
}

public sealed record EncryptedConnectorCredential(
    Guid CredentialId,
    string ConnectorType,
    string EncryptedValue);

public sealed record ConnectorKeyVerificationResult(
    ConnectorKeyVerificationCode Code,
    int CheckedCredentialCount,
    string Message)
{
    public bool IsSuccess => Code is ConnectorKeyVerificationCode.Success
        or ConnectorKeyVerificationCode.NoCredentials;
}

public interface IConnectorCredentialProbeSource
{
    Task<IReadOnlyList<EncryptedConnectorCredential>> ListEncryptedAsync(
        CancellationToken cancellationToken = default);
}

public interface IConnectorCredentialCipherProbe
{
    /// <summary>
    /// Decrypts into caller-owned bytes. The caller must zero the buffer after probing.
    /// Implementations must not log plaintext or ciphertext.
    /// </summary>
    byte[] DecryptToOwnedBuffer(string encryptedValue);
}

public sealed class ConnectorKeyMissingException : Exception
{
    public ConnectorKeyMissingException() : base("connector_key_missing") { }
}

/// <summary>
/// Content-free, fail-closed verifier suitable for a CLI/ops command.
/// It never returns provider names, usernames, credential values, or plaintext.
/// </summary>
public sealed class ConnectorKeyVerifier
{
    private readonly IConnectorCredentialProbeSource _source;
    private readonly IConnectorCredentialCipherProbe _cipher;

    public ConnectorKeyVerifier(
        IConnectorCredentialProbeSource source,
        IConnectorCredentialCipherProbe cipher)
    {
        _source = source;
        _cipher = cipher;
    }

    public async Task<ConnectorKeyVerificationResult> VerifyAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EncryptedConnectorCredential> credentials;
        try
        {
            credentials = await _source.ListEncryptedAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new(
                ConnectorKeyVerificationCode.StorageFailure,
                0,
                "Credential storage could not be read.");
        }

        if (credentials.Count == 0)
        {
            return new(
                ConnectorKeyVerificationCode.NoCredentials,
                0,
                "No encrypted connector credentials exist; key compatibility is not yet proven.");
        }

        var checkedCount = 0;
        foreach (var credential in credentials)
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[]? plaintext = null;
            try
            {
                plaintext = _cipher.DecryptToOwnedBuffer(credential.EncryptedValue);
                if (plaintext.Length == 0)
                {
                    return new(
                        ConnectorKeyVerificationCode.CorruptCiphertext,
                        checkedCount,
                        "A stored credential decrypted to an invalid empty payload.");
                }

                checkedCount++;
            }
            catch (ConnectorKeyMissingException)
            {
                return new(
                    ConnectorKeyVerificationCode.MissingKey,
                    checkedCount,
                    "The connector encryption key is not configured.");
            }
            catch (CryptographicException)
            {
                return new(
                    ConnectorKeyVerificationCode.InvalidKey,
                    checkedCount,
                    "The configured connector encryption key cannot decrypt all stored credentials.");
            }
            catch (FormatException)
            {
                return new(
                    ConnectorKeyVerificationCode.CorruptCiphertext,
                    checkedCount,
                    "A stored credential has an invalid ciphertext envelope.");
            }
            catch
            {
                return new(
                    ConnectorKeyVerificationCode.UnexpectedFailure,
                    checkedCount,
                    "Connector-key verification failed without exposing credential content.");
            }
            finally
            {
                if (plaintext is not null)
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
        }

        return new(
            ConnectorKeyVerificationCode.Success,
            checkedCount,
            $"Connector encryption key verified against {checkedCount} stored credential record(s).");
    }
}
