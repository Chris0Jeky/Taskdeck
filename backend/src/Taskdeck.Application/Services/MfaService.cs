using System.Security.Cryptography;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Manages TOTP-based MFA setup, verification, and status.
/// </summary>
public class MfaService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly MfaPolicySettings _policySettings;

    // TOTP constants
    private const int SecretByteLength = 20; // 160-bit secret per RFC 6238
    private const int TotpCodeLength = 6;
    private const string TotpAlgorithm = "SHA1"; // Standard for Google Authenticator compatibility
    private const string Issuer = "Taskdeck";

    public MfaService(IUnitOfWork unitOfWork, MfaPolicySettings policySettings)
    {
        _unitOfWork = unitOfWork;
        _policySettings = policySettings;
    }

    /// <summary>
    /// Returns the current MFA status for a user.
    /// </summary>
    public async Task<Result<MfaStatusDto>> GetStatusAsync(Guid userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null)
            return Result.Failure<MfaStatusDto>(ErrorCodes.NotFound, "User not found");

        return Result.Success(new MfaStatusDto(
            IsEnabled: user.MfaEnabled,
            IsSetupAvailable: _policySettings.EnableMfaSetup));
    }

    /// <summary>
    /// Initiates MFA setup by generating a new TOTP secret and recovery codes.
    /// Returns the secret and QR code URI for the user to scan.
    /// Any existing unconfirmed credential is replaced.
    /// </summary>
    public async Task<Result<MfaSetupDto>> SetupAsync(Guid userId)
    {
        if (!_policySettings.EnableMfaSetup)
            return Result.Failure<MfaSetupDto>(ErrorCodes.Forbidden, "MFA setup is not enabled on this instance");

        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null)
            return Result.Failure<MfaSetupDto>(ErrorCodes.NotFound, "User not found");

        if (user.MfaEnabled)
            return Result.Failure<MfaSetupDto>(ErrorCodes.Conflict, "MFA is already enabled. Disable it first to set up again.");

        // Remove any existing unconfirmed credential
        await _unitOfWork.MfaCredentials.DeleteByUserIdAsync(userId);

        // Generate new TOTP secret
        var secretBytes = RandomNumberGenerator.GetBytes(SecretByteLength);
        var secret = Base32Encode(secretBytes);

        // Generate recovery codes
        var recoveryCodes = GenerateRecoveryCodes(_policySettings.RecoveryCodeCount);
        var hashedRecoveryCodes = string.Join(",", recoveryCodes.Select(c => BCrypt.Net.BCrypt.HashPassword(c)));

        // Create and persist credential
        var credential = new MfaCredential(userId, secret);
        credential.SetRecoveryCodes(hashedRecoveryCodes);
        await _unitOfWork.MfaCredentials.AddAsync(credential);
        await _unitOfWork.SaveChangesAsync();

        // Build provisioning URI (otpauth://totp/Issuer:username?secret=...&issuer=...&digits=6&period=30)
        var qrCodeUri = $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(user.Username)}" +
                        $"?secret={secret}&issuer={Uri.EscapeDataString(Issuer)}&digits={TotpCodeLength}" +
                        $"&period={_policySettings.TotpTimeStepSeconds}";

        return Result.Success(new MfaSetupDto(secret, qrCodeUri, recoveryCodes));
    }

    /// <summary>
    /// Confirms MFA setup by validating a TOTP code from the user's authenticator app.
    /// </summary>
    public async Task<Result> ConfirmSetupAsync(Guid userId, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure(ErrorCodes.ValidationError, "Verification code is required");

        var credential = await _unitOfWork.MfaCredentials.GetByUserIdAsync(userId);
        if (credential == null)
            return Result.Failure(ErrorCodes.NotFound, "No MFA setup in progress. Initiate setup first.");

        if (credential.IsConfirmed)
            return Result.Failure(ErrorCodes.Conflict, "MFA is already confirmed");

        if (!ValidateTotp(credential.Secret, code))
            return Result.Failure(ErrorCodes.AuthenticationFailed, "Invalid verification code. Please try again.");

        credential.Confirm();

        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null)
            return Result.Failure(ErrorCodes.NotFound, "User not found");

        user.EnableMfa();
        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }

    /// <summary>
    /// Disables MFA for a user. Requires a valid TOTP code for security.
    /// </summary>
    public async Task<Result> DisableAsync(Guid userId, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure(ErrorCodes.ValidationError, "Verification code is required");

        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null)
            return Result.Failure(ErrorCodes.NotFound, "User not found");

        if (!user.MfaEnabled)
            return Result.Failure(ErrorCodes.ValidationError, "MFA is not enabled");

        var credential = await _unitOfWork.MfaCredentials.GetByUserIdAsync(userId);
        if (credential == null)
            return Result.Failure(ErrorCodes.NotFound, "MFA credential not found");

        if (!ValidateTotp(credential.Secret, code))
            return Result.Failure(ErrorCodes.AuthenticationFailed, "Invalid verification code");

        user.DisableMfa();
        await _unitOfWork.MfaCredentials.DeleteByUserIdAsync(userId);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }

    /// <summary>
    /// Validates a TOTP code for an authenticated user (used for sensitive action gates).
    /// </summary>
    public async Task<Result> VerifyCodeAsync(Guid userId, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure(ErrorCodes.ValidationError, "Verification code is required");

        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null)
            return Result.Failure(ErrorCodes.NotFound, "User not found");

        if (!user.MfaEnabled)
            return Result.Failure(ErrorCodes.ValidationError, "MFA is not enabled for this user");

        var credential = await _unitOfWork.MfaCredentials.GetByUserIdAsync(userId);
        if (credential == null || !credential.IsConfirmed)
            return Result.Failure(ErrorCodes.NotFound, "MFA credential not found or not confirmed");

        // Try TOTP code first
        if (ValidateTotp(credential.Secret, code))
            return Result.Success();

        // Try recovery code
        if (TryUseRecoveryCode(credential, code))
        {
            await _unitOfWork.SaveChangesAsync();
            return Result.Success();
        }

        return Result.Failure(ErrorCodes.AuthenticationFailed, "Invalid verification code");
    }

    /// <summary>
    /// Checks whether MFA verification is required for a sensitive action.
    /// </summary>
    public async Task<bool> IsMfaRequiredForSensitiveActionAsync(Guid userId)
    {
        if (!_policySettings.RequireMfaForSensitiveActions)
            return false;

        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        return user?.MfaEnabled == true;
    }

    // ── TOTP Implementation ─────────────────────────────────────────────

    internal bool ValidateTotp(string base32Secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != TotpCodeLength)
            return false;

        // Constant-time comparison within tolerance window
        var secretBytes = Base32Decode(base32Secret);
        var timeStep = _policySettings.TotpTimeStepSeconds;
        var currentStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / timeStep;

        for (var i = -_policySettings.TotpToleranceSteps; i <= _policySettings.TotpToleranceSteps; i++)
        {
            var expectedCode = ComputeTotp(secretBytes, currentStep + i);
            if (ConstantTimeEquals(code, expectedCode))
                return true;
        }

        return false;
    }

    private static string ComputeTotp(byte[] secret, long timeCounter)
    {
        var counterBytes = BitConverter.GetBytes(timeCounter);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counterBytes);

        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counterBytes);

        var offset = hash[^1] & 0x0F;
        var truncatedHash = (hash[offset] & 0x7F) << 24
                          | (hash[offset + 1] & 0xFF) << 16
                          | (hash[offset + 2] & 0xFF) << 8
                          | (hash[offset + 3] & 0xFF);

        var code = truncatedHash % 1_000_000;
        return code.ToString("D6");
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
            return false;

        var result = 0;
        for (var i = 0; i < a.Length; i++)
            result |= a[i] ^ b[i];

        return result == 0;
    }

    private bool TryUseRecoveryCode(MfaCredential credential, string code)
    {
        if (string.IsNullOrWhiteSpace(credential.RecoveryCodes))
            return false;

        var hashedCodes = credential.RecoveryCodes.Split(',').ToList();
        for (var i = 0; i < hashedCodes.Count; i++)
        {
            if (!BCrypt.Net.BCrypt.Verify(code, hashedCodes[i]))
                continue;

            // Remove used recovery code
            hashedCodes.RemoveAt(i);
            credential.SetRecoveryCodes(hashedCodes.Count > 0
                ? string.Join(",", hashedCodes)
                : " "); // Keep non-empty to satisfy domain validation
            return true;
        }

        return false;
    }

    // ── Base32 Encoding/Decoding ────────────────────────────────────────

    private static readonly char[] Base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".ToCharArray();

    internal static string Base32Encode(byte[] data)
    {
        var result = new char[(data.Length * 8 + 4) / 5];
        var buffer = 0;
        var bitsLeft = 0;
        var index = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;

            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                result[index++] = Base32Chars[(buffer >> bitsLeft) & 0x1F];
            }
        }

        if (bitsLeft > 0)
        {
            buffer <<= (5 - bitsLeft);
            result[index] = Base32Chars[buffer & 0x1F];
        }

        return new string(result);
    }

    internal static byte[] Base32Decode(string input)
    {
        input = input.TrimEnd('=').ToUpperInvariant();
        var output = new byte[input.Length * 5 / 8];
        var buffer = 0;
        var bitsLeft = 0;
        var index = 0;

        foreach (var c in input)
        {
            var value = c switch
            {
                >= 'A' and <= 'Z' => c - 'A',
                >= '2' and <= '7' => c - '2' + 26,
                _ => throw new ArgumentException($"Invalid base32 character: {c}")
            };

            buffer = (buffer << 5) | value;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output[index++] = (byte)(buffer >> bitsLeft);
            }
        }

        return output[..index];
    }

    private static string[] GenerateRecoveryCodes(int count)
    {
        var codes = new string[count];
        for (var i = 0; i < count; i++)
        {
            // Generate an 8-character alphanumeric recovery code in two groups: XXXX-XXXX
            var bytes = RandomNumberGenerator.GetBytes(5);
            var hex = Convert.ToHexString(bytes).ToUpperInvariant()[..8];
            codes[i] = $"{hex[..4]}-{hex[4..8]}";
        }
        return codes;
    }
}
