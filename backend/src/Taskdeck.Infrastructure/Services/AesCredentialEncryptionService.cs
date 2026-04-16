using System.Security.Cryptography;
using Taskdeck.Application.Connectors;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// AES-256-CBC encryption service for connector credentials.
/// Key material is sourced from configuration; plaintext secrets never reach the DB.
/// </summary>
public sealed class AesCredentialEncryptionService : ICredentialEncryptionService
{
    private readonly byte[] _key;

    /// <summary>
    /// Create an encryption service with the given base64-encoded 256-bit key.
    /// </summary>
    public AesCredentialEncryptionService(string base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
            throw new ArgumentException("Encryption key must not be empty.", nameof(base64Key));

        _key = Convert.FromBase64String(base64Key);

        if (_key.Length != 32) // 256 bits
            throw new ArgumentException("Encryption key must be exactly 256 bits (32 bytes).", nameof(base64Key));
    }

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            throw new ArgumentException("Plaintext must not be empty.", nameof(plaintext));

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        // Prepend IV to ciphertext for self-contained storage.
        var combined = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, combined, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, combined, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(combined);
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            throw new ArgumentException("Ciphertext must not be empty.", nameof(ciphertext));

        var combined = Convert.FromBase64String(ciphertext);

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        const int ivLength = 16; // AES block size
        if (combined.Length < ivLength + 1)
            throw new CryptographicException("Ciphertext is too short to contain IV and data.");

        var iv = new byte[ivLength];
        Buffer.BlockCopy(combined, 0, iv, 0, ivLength);
        aes.IV = iv;

        var cipherBytes = new byte[combined.Length - ivLength];
        Buffer.BlockCopy(combined, ivLength, cipherBytes, 0, cipherBytes.Length);

        using var decryptor = aes.CreateDecryptor();
        var plaintextBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return System.Text.Encoding.UTF8.GetString(plaintextBytes);
    }
}
