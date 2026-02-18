namespace Taskdeck.Application.Services;

public sealed class LlmProviderSettings
{
    public bool EnableLiveProviders { get; set; }
    public bool AllowLiveProvidersInDevelopment { get; set; }
    public string Provider { get; set; } = "Mock";
    public OpenAiProviderSettings OpenAi { get; set; } = new();
}

public sealed class OpenAiProviderSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o-mini";
    public int TimeoutSeconds { get; set; } = 30;
}
