using Microsoft.Extensions.Options;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Validation;

/// <summary>
/// Custom validation for <see cref="JwtSettings"/>.
/// Ensures SecretKey is populated (by either config or FirstRunBootstrapper)
/// and meets minimum length for security.
/// </summary>
public sealed class JwtSettingsValidator : IValidateOptions<JwtSettings>
{
    /// <summary>
    /// Minimum key length in characters for HMAC-SHA256, sourced from the single authoritative
    /// floor on <see cref="JwtSettings.MinSecretKeyLength"/> so this validator and the
    /// authentication registration never diverge. A 256-bit key is 32 bytes.
    /// </summary>
    private const int MinSecretKeyLength = JwtSettings.MinSecretKeyLength;

    public ValidateOptionsResult Validate(string? name, JwtSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            failures.Add(
                "Jwt:SecretKey is empty. Either configure it explicitly or ensure " +
                "FirstRunBootstrapper runs before validation (non-Development environments).");
        }
        else if (options.SecretKey.Length < MinSecretKeyLength)
        {
            failures.Add(
                $"Jwt:SecretKey is too short ({options.SecretKey.Length} chars). " +
                $"Minimum length is {MinSecretKeyLength} characters for adequate security.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
