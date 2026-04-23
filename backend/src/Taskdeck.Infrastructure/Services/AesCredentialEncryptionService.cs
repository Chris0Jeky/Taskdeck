using System.Security.Cryptography;
using Taskdeck.Application.Connectors;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// AES-256-GCM (AEAD) encryption service for connector credentials.
/// Key material is sourced from configuration; plaintext secrets never reach the DB.
///
/// Storage format (base64-encoded): [12-byte nonce][16-byte tag][ciphertext]
/// </summary>
public sealed class AesCredentialEncryptionService : ICredentialEncryptionService
{
    private const int NonceSize = 12; // AES-GCM standard nonce size
    private const int TagSize = 16;   // AES-GCM standard tag size (128-bit)

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

        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var cipherBytes = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aesGcm = new AesGcm(_key, TagSize);
        aesGcm.Encrypt(nonce, plaintextBytes, cipherBytes, tag);

        // Combined format: [nonce][tag][ciphertext]
        var combined = new byte[NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, combined, NonceSize, TagSize);
        Buffer.BlockCopy(cipherBytes, 0, combined, NonceSize + TagSize, cipherBytes.Length);

        return Convert.ToBase64String(combined);
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            throw new ArgumentException("Ciphertext must not be empty.", nameof(ciphertext));

        var combined = Convert.FromBase64String(ciphertext);

        if (combined.Length < NonceSize + TagSize + 1)
            throw new CryptographicException("Ciphertext is too short to contain nonce, tag, and data.");

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipherBytes = new byte[combined.Length - NonceSize - TagSize];

        Buffer.BlockCopy(combined, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(combined, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(combined, NonceSize + TagSize, cipherBytes, 0, cipherBytes.Length);

        var plaintextBytes = new byte[cipherBytes.Length];

        using var aesGcm = new AesGcm(_key, TagSize);
        aesGcm.Decrypt(nonce, cipherBytes, tag, plaintextBytes);

        return System.Text.Encoding.UTF8.GetString(plaintextBytes);
    }
}
