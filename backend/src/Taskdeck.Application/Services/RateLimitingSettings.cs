using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

public sealed class RateLimitingSettings
{
    public bool Enabled { get; set; } = true;

    [Required]
    public RateLimitPolicySettings AuthPerIp { get; set; } = new(20, 60);

    [Required]
    public RateLimitPolicySettings HotPathPerUser { get; set; } = new(30, 60);

    [Required]
    public RateLimitPolicySettings CaptureWritePerUser { get; set; } = new(10, 60);

    /// <summary>
    /// Rate limit for note import endpoints. Lower than CaptureWritePerUser because
    /// each import request can create up to 50 capture items.
    /// </summary>
    [Required]
    public RateLimitPolicySettings NoteImportPerUser { get; set; } = new(5, 60);

    [Required]
    public RateLimitPolicySettings McpPerApiKey { get; set; } = new(60, 60);

    /// <summary>
    /// Pre-authentication FAILURE budget for MCP HTTP attempts from one client address. A permit
    /// is spent only when authentication fails (401); once spent, further attempts from the
    /// address are rejected before missing/invalid-key database work. Valid requests never spend
    /// this budget — the per-key policy is their only throttle.
    /// </summary>
    [Required]
    public RateLimitPolicySettings McpAuthenticationPerIp { get; set; } = new(120, 60);

    /// <summary>
    /// Maximum concurrent in-flight /mcp requests per client address admitted past the
    /// pre-authentication gate. The failure budget bounds cumulative failures per window; this cap
    /// bounds instantaneous pre-auth (key parse + database lookup) concurrency, so failed-auth
    /// lookups per address per window never exceed the failure PermitLimit plus this cap.
    /// <para>
    /// Deliberately NOT range-checked via a data annotation: <c>ValidateDataAnnotations()</c> would
    /// reject an out-of-range value even when <see cref="Enabled"/> is <c>false</c> and the limiter
    /// is never constructed. The range (1-10000) is enforced by the API layer's
    /// <c>RateLimitingSettingsValidator</c>, which — like the nested policy settings — skips
    /// validation while rate limiting is disabled and fails startup fast when it is enabled.
    /// </para>
    /// </summary>
    public int McpAuthenticationPerIpConcurrency { get; set; } = 16;

    /// <summary>
    /// Rate limit for token refresh endpoint. Tight limit to prevent token farming:
    /// max 5 refreshes per 60 seconds per user.
    /// </summary>
    [Required]
    public RateLimitPolicySettings TokenRefreshPerUser { get; set; } = new(5, 60);
}

public sealed class RateLimitPolicySettings
{
    public const int MinPermitLimit = 1;
    public const int MaxPermitLimit = 10000;
    public const int MinWindowSeconds = 1;
    public const int MaxWindowSeconds = 86400;

    public RateLimitPolicySettings()
    {
    }

    public RateLimitPolicySettings(int permitLimit, int windowSeconds)
    {
        PermitLimit = permitLimit;
        WindowSeconds = windowSeconds;
    }

    [Range(MinPermitLimit, MaxPermitLimit, ErrorMessage = "PermitLimit must be between 1 and 10000.")]
    public int PermitLimit { get; set; }

    [Range(MinWindowSeconds, MaxWindowSeconds, ErrorMessage = "WindowSeconds must be between 1 and 86400 (1 day).")]
    public int WindowSeconds { get; set; }
}
