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
}

public sealed class RateLimitPolicySettings
{
    public RateLimitPolicySettings()
    {
    }

    public RateLimitPolicySettings(int permitLimit, int windowSeconds)
    {
        PermitLimit = permitLimit;
        WindowSeconds = windowSeconds;
    }

    [Range(1, 10000, ErrorMessage = "PermitLimit must be between 1 and 10000.")]
    public int PermitLimit { get; set; }

    [Range(1, 86400, ErrorMessage = "WindowSeconds must be between 1 and 86400 (1 day).")]
    public int WindowSeconds { get; set; }
}
