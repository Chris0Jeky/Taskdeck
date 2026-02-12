namespace Taskdeck.Application.Services;

public class JwtSettings
{
    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "Taskdeck";
    public string Audience { get; set; } = "TaskdeckUsers";
    public int ExpirationMinutes { get; set; } = 1440; // 24 hours
}
