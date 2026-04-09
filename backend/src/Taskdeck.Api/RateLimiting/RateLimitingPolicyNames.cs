namespace Taskdeck.Api.RateLimiting;

public static class RateLimitingPolicyNames
{
    public const string AuthPerIp = "AuthPerIp";
    public const string HotPathPerUser = "HotPathPerUser";
    public const string CaptureWritePerUser = "CaptureWritePerUser";
    public const string McpPerApiKey = "McpPerApiKey";
}
