using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

public sealed class LlmProviderSettings
{
    public bool EnableLiveProviders { get; set; }
    public bool AllowLiveProvidersInDevelopment { get; set; }

    [Required(AllowEmptyStrings = false)]
    [RegularExpression("^(?i)(Mock|OpenAi|OpenAiCompatible|Gemini|Ollama)$", ErrorMessage = "Llm Provider must be 'Mock', 'OpenAi', 'OpenAiCompatible', 'Gemini', or 'Ollama' (case-insensitive).")]
    public string Provider { get; set; } = "Mock";

    [Required]
    public OpenAiProviderSettings OpenAi { get; set; } = new();

    [Required]
    public GeminiProviderSettings Gemini { get; set; } = new();

    /// <summary>
    /// Settings for an OpenAI chat-completions compatible endpoint such as
    /// OpenRouter, Groq, or DeepSeek. Unlike <see cref="OpenAi"/>, this has
    /// no vendor default: callers must select and configure an endpoint.
    /// </summary>
    public OpenAiCompatibleProviderSettings OpenAiCompatible { get; set; } = new();

    public OllamaProviderSettings Ollama { get; set; } = new();
}

public sealed record LlmProviderRuntimePolicy(
    bool AllowGeneralProviderLocalhost,
    bool AllowOllamaLocalhost,
    bool ProtectOutboundTelemetry = false);

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

public sealed class OpenAiCompatibleProviderSettings
{
    /// <summary>
    /// API key accepted by the configured OpenAI-compatible endpoint.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Required OpenAI-compatible API base URL (for example, https://openrouter.ai/api/v1).
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Required vendor model identifier.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    [Range(1, 300, ErrorMessage = "TimeoutSeconds must be between 1 and 300.")]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Optional request headers required by a compatible gateway (for example,
    /// OpenRouter's HTTP-Referer and X-Title headers). Authorization is always
    /// derived from <see cref="ApiKey"/> and cannot be supplied here.
    /// </summary>
    public Dictionary<string, string> ExtraHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
