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
    /// Aggregate pre-authentication limit for MCP HTTP attempts from one client address.
    /// This bounds missing/invalid-key database work before the per-key policy can run.
    /// </summary>
    [Required]
    public RateLimitPolicySettings McpAuthenticationPerIp { get; set; } = new(120, 60);

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
