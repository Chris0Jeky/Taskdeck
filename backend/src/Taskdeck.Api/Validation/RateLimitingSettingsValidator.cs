using Microsoft.Extensions.Options;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Validation;

/// <summary>
/// Cross-property and nested object validation for <see cref="RateLimitingSettings"/>.
/// <c>ValidateDataAnnotations()</c> does not recurse into nested objects, so this
/// validator enforces <see cref="RateLimitPolicySettings"/> constraints explicitly.
/// </summary>
public sealed class RateLimitingSettingsValidator : IValidateOptions<RateLimitingSettings>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingSettings options)
    {
        // When rate limiting is disabled, skip nested policy validation.
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        ValidatePolicy(failures, nameof(options.AuthPerIp), options.AuthPerIp);
        ValidatePolicy(failures, nameof(options.HotPathPerUser), options.HotPathPerUser);
        ValidatePolicy(failures, nameof(options.CaptureWritePerUser), options.CaptureWritePerUser);
        ValidatePolicy(failures, nameof(options.NoteImportPerUser), options.NoteImportPerUser);
        ValidatePolicy(failures, nameof(options.McpPerApiKey), options.McpPerApiKey);
        ValidatePolicy(failures, nameof(options.TokenRefreshPerUser), options.TokenRefreshPerUser);

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidatePolicy(List<string> failures, string policyName, RateLimitPolicySettings? policy)
    {
        if (policy is null)
        {
            failures.Add($"RateLimiting:{policyName} is required.");
            return;
        }

        if (policy.PermitLimit < RateLimitPolicySettings.MinPermitLimit || policy.PermitLimit > RateLimitPolicySettings.MaxPermitLimit)
        {
            failures.Add($"RateLimiting:{policyName}:PermitLimit must be between {RateLimitPolicySettings.MinPermitLimit} and {RateLimitPolicySettings.MaxPermitLimit} (was {policy.PermitLimit}).");
        }

        if (policy.WindowSeconds < RateLimitPolicySettings.MinWindowSeconds || policy.WindowSeconds > RateLimitPolicySettings.MaxWindowSeconds)
        {
            failures.Add($"RateLimiting:{policyName}:WindowSeconds must be between {RateLimitPolicySettings.MinWindowSeconds} and {RateLimitPolicySettings.MaxWindowSeconds} (was {policy.WindowSeconds}).");
        }
    }
}
