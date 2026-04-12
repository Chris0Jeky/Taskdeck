namespace Taskdeck.Application.Services;

public sealed class RateLimitingSettings
{
    public bool Enabled { get; set; } = true;
    public RateLimitPolicySettings AuthPerIp { get; set; } = new(20, 60);
    public RateLimitPolicySettings HotPathPerUser { get; set; } = new(30, 60);
    public RateLimitPolicySettings CaptureWritePerUser { get; set; } = new(10, 60);
    /// <summary>
    /// Rate limit for note import endpoints. Lower than CaptureWritePerUser because
    /// each import request can create up to 50 capture items.
    /// </summary>
    public RateLimitPolicySettings NoteImportPerUser { get; set; } = new(5, 60);
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

    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
}
