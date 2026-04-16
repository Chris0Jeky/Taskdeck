using System.Security.Cryptography;
using FluentAssertions;
using Taskdeck.Infrastructure.Services;
using Xunit;

namespace Taskdeck.Api.Tests;

public class AesCredentialEncryptionServiceTests
{
    /// <summary>
    /// Generate a valid base64-encoded 256-bit key for testing.
    /// </summary>
    private static string GenerateTestKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return Convert.ToBase64String(key);
    }

    [Fact]
    public void Encrypt_Decrypt_RoundTrip_ShouldReturnOriginalPlaintext()
    {
        var key = GenerateTestKey();
        var service = new AesCredentialEncryptionService(key);

        const string original = "ghp_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";
        var encrypted = service.Encrypt(original);
        var decrypted = service.Decrypt(encrypted);

        decrypted.Should().Be(original);
    }

    [Fact]
    public void Encrypt_ShouldProduceDifferentCiphertextEachTime()
    {
        var key = GenerateTestKey();
        var service = new AesCredentialEncryptionService(key);

        const string plaintext = "my-secret-token";
        var encrypted1 = service.Encrypt(plaintext);
        var encrypted2 = service.Encrypt(plaintext);

        // Nonce is random each time, so ciphertexts should differ.
        encrypted1.Should().NotBe(encrypted2);

        // Both should still decrypt to the same value.
        service.Decrypt(encrypted1).Should().Be(plaintext);
        service.Decrypt(encrypted2).Should().Be(plaintext);
    }

    [Fact]
    public void Decrypt_WithTamperedCiphertext_ShouldThrowCryptographicException()
    {
        var key = GenerateTestKey();
        var service = new AesCredentialEncryptionService(key);

        var encrypted = service.Encrypt("my-secret");
        var bytes = Convert.FromBase64String(encrypted);

        // Tamper with the ciphertext portion (after nonce+tag = 12+16 = 28 bytes).
        if (bytes.Length > 28)
        {
            bytes[28] ^= 0xFF;
        }

        var tampered = Convert.ToBase64String(bytes);

        // AES-GCM should detect the tampering and throw.
        var act = () => service.Decrypt(tampered);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_WithTamperedTag_ShouldThrowCryptographicException()
    {
        var key = GenerateTestKey();
        var service = new AesCredentialEncryptionService(key);

        var encrypted = service.Encrypt("my-secret");
        var bytes = Convert.FromBase64String(encrypted);

        // Tamper with the authentication tag (bytes 12-27).
        bytes[12] ^= 0xFF;

        var tampered = Convert.ToBase64String(bytes);

        var act = () => service.Decrypt(tampered);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_WithTooShortCiphertext_ShouldThrowCryptographicException()
    {
        var key = GenerateTestKey();
        var service = new AesCredentialEncryptionService(key);

        // Less than nonce(12) + tag(16) + 1 = 29 bytes.
        var tooShort = Convert.ToBase64String(new byte[28]);

        var act = () => service.Decrypt(tooShort);
        act.Should().Throw<CryptographicException>()
            .WithMessage("*too short*");
    }

    [Fact]
    public void Constructor_WithEmptyKey_ShouldThrowArgumentException()
    {
        var act = () => new AesCredentialEncryptionService("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithWrongSizeKey_ShouldThrowArgumentException()
    {
        // 128-bit key (16 bytes) should be rejected.
        var shortKey = Convert.ToBase64String(new byte[16]);
        var act = () => new AesCredentialEncryptionService(shortKey);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*256 bits*");
    }

    [Fact]
    public void Encrypt_WithEmptyPlaintext_ShouldThrowArgumentException()
    {
        var service = new AesCredentialEncryptionService(GenerateTestKey());
        var act = () => service.Encrypt("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decrypt_WithEmptyCiphertext_ShouldThrowArgumentException()
    {
        var service = new AesCredentialEncryptionService(GenerateTestKey());
        var act = () => service.Decrypt("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Encrypt_Decrypt_ShouldHandleUnicodeContent()
    {
        var service = new AesCredentialEncryptionService(GenerateTestKey());
        const string unicode = "p\u00e4ssw\u00f6rd-\u2603-\ud83d\udd11";

        var encrypted = service.Encrypt(unicode);
        var decrypted = service.Decrypt(encrypted);

        decrypted.Should().Be(unicode);
    }

    [Fact]
    public void Encrypt_Decrypt_ShouldHandleLongContent()
    {
        var service = new AesCredentialEncryptionService(GenerateTestKey());
        var longContent = new string('A', 4096);

        var encrypted = service.Encrypt(longContent);
        var decrypted = service.Decrypt(encrypted);

        decrypted.Should().Be(longContent);
    }

    [Fact]
    public void Decrypt_WithDifferentKey_ShouldThrowCryptographicException()
    {
        var key1 = GenerateTestKey();
        var key2 = GenerateTestKey();

        var service1 = new AesCredentialEncryptionService(key1);
        var service2 = new AesCredentialEncryptionService(key2);

        var encrypted = service1.Encrypt("secret");

        // Decrypting with a different key should fail authentication.
        var act = () => service2.Decrypt(encrypted);
        act.Should().Throw<CryptographicException>();
    }
}
