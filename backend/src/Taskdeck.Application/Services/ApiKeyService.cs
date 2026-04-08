using System.Security.Cryptography;
using System.Text;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Manages API key lifecycle: creation, validation, listing, and revocation.
/// Keys use the <c>tdsk_</c> prefix and are SHA-256 hashed at rest.
/// </summary>
public class ApiKeyService
{
    private const string Base62Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int RandomPartLength = 36;

    private readonly IUnitOfWork _unitOfWork;

    public ApiKeyService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Create a new API key for the given user.
    /// Returns the plaintext key (shown once) and the persisted entity.
    /// </summary>
    public async Task<(string PlaintextKey, ApiKey Entity)> CreateKeyAsync(
        Guid userId,
        string name,
        TimeSpan? expiresIn = null,
        CancellationToken cancellationToken = default)
    {
        // Verify user exists
        var user = await _unitOfWork.Users.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            throw new DomainException(ErrorCodes.NotFound, "User not found");

        var plaintextKey = GenerateKey();
        var keyHash = HashKey(plaintextKey);
        var keyPrefix = plaintextKey[..8]; // "tdsk_" + first 3 random chars

        DateTimeOffset? expiresAt = expiresIn.HasValue
            ? DateTimeOffset.UtcNow.Add(expiresIn.Value)
            : null;

        var apiKey = new ApiKey(userId, keyHash, keyPrefix, name, expiresAt);

        await _unitOfWork.ApiKeys.AddAsync(apiKey, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return (plaintextKey, apiKey);
    }

    /// <summary>
    /// Validate an API key and return the associated entity if valid.
    /// Returns null if the key is invalid, expired, or revoked.
    /// </summary>
    public async Task<ApiKey?> ValidateKeyAsync(string plaintextKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey) || !plaintextKey.StartsWith(ApiKey.KeyPrefix))
            return null;

        var keyHash = HashKey(plaintextKey);
        var apiKey = await _unitOfWork.ApiKeys.GetByKeyHashAsync(keyHash, cancellationToken);

        if (apiKey is null || !apiKey.IsActive)
            return null;

        // Record usage (fire-and-forget style, non-critical)
        apiKey.RecordUsage();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return apiKey;
    }

    /// <summary>List all API keys for a user.</summary>
    public async Task<IEnumerable<ApiKey>> ListKeysAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ApiKeys.GetByUserIdAsync(userId, cancellationToken);
    }

    /// <summary>Revoke an API key.</summary>
    public async Task RevokeKeyAsync(Guid keyId, Guid userId, CancellationToken cancellationToken = default)
    {
        var apiKey = await _unitOfWork.ApiKeys.GetByIdAsync(keyId, cancellationToken);
        if (apiKey is null)
            throw new DomainException(ErrorCodes.NotFound, "API key not found");

        if (apiKey.UserId != userId)
            throw new DomainException(ErrorCodes.Forbidden, "Cannot revoke another user's API key");

        apiKey.Revoke();
        await _unitOfWork.ApiKeys.UpdateAsync(apiKey, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Generate a plaintext API key with tdsk_ prefix.</summary>
    public static string GenerateKey()
    {
        var sb = new StringBuilder(ApiKey.KeyPrefix, ApiKey.RawKeyLength);
        for (int i = 0; i < RandomPartLength; i++)
        {
            // Use RandomNumberGenerator.GetInt32 to avoid modulo bias
            sb.Append(Base62Chars[RandomNumberGenerator.GetInt32(Base62Chars.Length)]);
        }
        return sb.ToString();
    }

    /// <summary>Compute SHA-256 hash of a plaintext key, returned as lowercase hex.</summary>
    public static string HashKey(string plaintextKey)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintextKey);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
