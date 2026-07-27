using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

public sealed class LlmProviderSettings
{
    public bool EnableLiveProviders { get; set; }
    public bool AllowLiveProvidersInDevelopment { get; set; }

    [Required(AllowEmptyStrings = false)]
    [RegularExpression("^(?i)(Mock|OpenAi|Gemini|Ollama)$", ErrorMessage = "Llm Provider must be 'Mock', 'OpenAi', 'Gemini', or 'Ollama' (case-insensitive).")]
    public string Provider { get; set; } = "Mock";

    [Required]
    public OpenAiProviderSettings OpenAi { get; set; } = new();

    [Required]
    public GeminiProviderSettings Gemini { get; set; } = new();

    public OllamaProviderSettings Ollama { get; set; } = new();
}

public sealed record LlmProviderRuntimePolicy(
    bool AllowGeneralProviderLocalhost,
    bool AllowOllamaLocalhost);

public sealed class OpenAiProviderSettings
{
    /// <summary>
    /// API key for OpenAI. Only required when the provider is set to "OpenAi".
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [Url]
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    [Required(AllowEmptyStrings = false)]
    public string Model { get; set; } = "gpt-4o-mini";

    [Range(1, 300, ErrorMessage = "TimeoutSeconds must be between 1 and 300.")]
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class GeminiProviderSettings
{
    /// <summary>
    /// API key for Gemini. Only required when the provider is set to "Gemini".
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [Url]
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    [Required(AllowEmptyStrings = false)]
    public string Model { get; set; } = "gemini-2.5-flash";

    [Range(1, 300, ErrorMessage = "TimeoutSeconds must be between 1 and 300.")]
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class OllamaProviderSettings
{
    [Required(AllowEmptyStrings = false)]
    [Url]
    public string BaseUrl { get; set; } = "http://localhost:11434";

    [Required(AllowEmptyStrings = false)]
    public string Model { get; set; } = "llama3.2";

    [Range(1, 600, ErrorMessage = "TimeoutSeconds must be between 1 and 600.")]
    public int TimeoutSeconds { get; set; } = 120;

    public bool AllowLocalhostEndpoints { get; set; }
}
