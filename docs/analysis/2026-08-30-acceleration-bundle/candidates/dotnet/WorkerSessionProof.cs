using System;
using System.Security.Cryptography;
using System.Text;

namespace Taskdeck.Acceleration.Candidates.Processing;

/// <summary>
/// Per-process challenge/proof helper. The raw secret is passed by protected transport
/// (for example an environment variable) and is never placed in a protocol envelope.
/// </summary>
public static class WorkerSessionProof
{
    public const string SecretEnvironmentVariable = "TASKDECK_PROCESSOR_SESSION_SECRET";

    public static string CreateSecret() => CreateRandomBase64Url(32);
    public static string CreateChallenge() => CreateRandomBase64Url(24);

    public static string ComputeProof(
        string secretBase64Url,
        string challenge,
        string protocolVersion,
        string processorId)
    {
        if (string.IsNullOrWhiteSpace(challenge)) throw new ArgumentException("challenge_required", nameof(challenge));
        if (string.IsNullOrWhiteSpace(protocolVersion)) throw new ArgumentException("protocol_version_required", nameof(protocolVersion));
        if (string.IsNullOrWhiteSpace(processorId)) throw new ArgumentException("processor_id_required", nameof(processorId));

        var secret = DecodeBase64Url(secretBase64Url);
        var payload = Encoding.UTF8.GetBytes($"{protocolVersion}\n{challenge}\n{processorId}");
        byte[]? digest = null;
        try
        {
            using var hmac = new HMACSHA256(secret);
            digest = hmac.ComputeHash(payload);
            return EncodeBase64Url(digest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            CryptographicOperations.ZeroMemory(payload);
            if (digest is not null)
            {
                CryptographicOperations.ZeroMemory(digest);
            }
        }
    }

    public static bool VerifyProof(
        string secretBase64Url,
        string challenge,
        string protocolVersion,
        string processorId,
        string suppliedProof)
    {
        byte[]? expected = null;
        byte[]? supplied = null;
        try
        {
            expected = DecodeBase64Url(ComputeProof(secretBase64Url, challenge, protocolVersion, processorId));
            supplied = DecodeBase64Url(suppliedProof);
            return expected.Length == supplied.Length
                   && CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        catch (FormatException)
        {
            return false;
        }
        finally
        {
            if (expected is not null) CryptographicOperations.ZeroMemory(expected);
            if (supplied is not null) CryptographicOperations.ZeroMemory(supplied);
        }
    }

    private static string CreateRandomBase64Url(int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        try
        {
            return EncodeBase64Url(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string EncodeBase64Url(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] DecodeBase64Url(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new FormatException("base64url_empty");
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return Convert.FromBase64String(normalized);
    }
}
