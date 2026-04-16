namespace Taskdeck.Application.Connectors;

/// <summary>
/// Encrypts and decrypts credential values.
/// Implemented in Infrastructure layer with AES-256.
/// </summary>
public interface ICredentialEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}
