namespace Taskdeck.Application.Services;

public enum LlmProviderKind
{
    Mock = 0,
    OpenAi = 1,
    Ollama = 3,
    OpenAiCompatible = 4
}

public sealed record LlmProviderDecision(LlmProviderKind ProviderKind, string Reason);

public static class LlmProviderSelectionPolicy
{
    public const string RetiredGeminiProviderMessage =
        "Gemini provider support was removed from Taskdeck. Set Llm:Provider (or Llm__Provider) " +
        "to OpenAi, OpenAiCompatible, Ollama, or Mock, and remove the retired Gemini settings section under Llm.";

    private static readonly HashSet<string> ForbiddenCompatibleHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Connection", "Transfer-Encoding", "Cookie", "Cookie2", "Keep-Alive",
        "TE", "Trailer", "Upgrade", "Via", "Forwarded", "Content-Length", "HTTP2-Settings"
    };

    public static LlmProviderDecision Evaluate(LlmProviderSettings settings, string? environmentName)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfRetiredProvider(settings.Provider);

        var requestedProvider = ResolveRequestedProviderKind(settings.Provider);
        if (!requestedProvider.HasValue)
        {
            return new LlmProviderDecision(
                LlmProviderKind.Mock,
                $"Provider mode '{settings.Provider}' resolves to mock.");
        }

        if (requestedProvider.Value == LlmProviderKind.Mock)
        {
            return new LlmProviderDecision(
                LlmProviderKind.Mock,
                "Mock provider selected.");
        }

        if (!settings.EnableLiveProviders)
        {
            return new LlmProviderDecision(
                LlmProviderKind.Mock,
                "Live providers are disabled by configuration.");
        }

        if (IsDevelopmentLike(environmentName) && !settings.AllowLiveProvidersInDevelopment)
        {
            return new LlmProviderDecision(
                LlmProviderKind.Mock,
                "Live providers are disabled for development-like environments.");
        }

        // In development with AllowLiveProvidersInDevelopment, permit localhost endpoints
        // for live-provider gateways. Ollama also has an explicit localhost flag because
        // its default BaseUrl is localhost and runtime validation enforces that opt-in.
        var allowDevelopmentLocalhostEndpoints = IsDevelopmentLike(environmentName) && settings.AllowLiveProvidersInDevelopment;

        if (requestedProvider.Value == LlmProviderKind.OpenAi)
        {
            if (!TryValidateOpenAiSettings(settings, out var validationError, allowDevelopmentLocalhostEndpoints))
            {
                return new LlmProviderDecision(
                    LlmProviderKind.Mock,
                    $"OpenAI configuration is invalid: {validationError}");
            }

            return new LlmProviderDecision(
                LlmProviderKind.OpenAi,
                "OpenAI provider selected.");
        }

        if (requestedProvider.Value == LlmProviderKind.OpenAiCompatible)
        {
            if (!TryValidateOpenAiCompatibleSettings(settings, out var compatibleValidationError, allowDevelopmentLocalhostEndpoints))
            {
                return new LlmProviderDecision(
                    LlmProviderKind.Mock,
                    $"OpenAI-compatible configuration is invalid: {compatibleValidationError}");
            }

            return new LlmProviderDecision(
                LlmProviderKind.OpenAiCompatible,
                "OpenAI-compatible provider selected.");
        }

        var allowOllamaLocalhostEndpoints =
            allowDevelopmentLocalhostEndpoints &&
            settings.Ollama?.AllowLocalhostEndpoints == true;

        if (!TryValidateOllamaSettings(settings, out var ollamaValidationError, allowOllamaLocalhostEndpoints))
        {
            return new LlmProviderDecision(
                LlmProviderKind.Mock,
                $"Ollama configuration is invalid: {ollamaValidationError}");
        }

        return new LlmProviderDecision(
            LlmProviderKind.Ollama,
            "Ollama provider selected.");
    }

    public static void ThrowIfRetiredProvider(string? provider)
    {
        if (provider?.Trim().Equals("Gemini", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new RetiredLlmProviderConfigurationException(
                RetiredLlmProviderConfigurationReason.ProviderSelector,
                RetiredGeminiProviderMessage);
        }
    }

    internal static bool IsExplicitlySupportedProvider(string? provider)
        => !string.IsNullOrWhiteSpace(provider) && ResolveRequestedProviderKind(provider).HasValue;

    public static bool TryValidateOpenAiSettings(
        LlmProviderSettings settings,
        out string error,
        bool allowLocalhostEndpoints = false)
    {
        if (settings.OpenAi is null)
        {
            error = "OpenAI settings are required.";
            return false;
        }

        var openAi = settings.OpenAi;

        if (string.IsNullOrWhiteSpace(openAi.ApiKey))
        {
            error = "ApiKey is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(openAi.Model))
        {
            error = "Model is required.";
            return false;
        }

        if (!Uri.TryCreate(openAi.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            error = "BaseUrl must be an absolute HTTP(S) URI.";
            return false;
        }

        // SSRF protection: block private IP ranges, cloud metadata endpoints, and internal hostnames.
        // In development with AllowLiveProvidersInDevelopment, localhost is permitted for local
        // LLM gateways (Ollama, LM Studio, etc.).
        var ssrfResult = SsrfProtectionService.ValidateLlmProviderUrl(openAi.BaseUrl, allowLocalhostEndpoints);
        if (!ssrfResult.IsAllowed)
        {
            error = $"BaseUrl blocked by SSRF protection: {ssrfResult.ErrorMessage}";
            return false;
        }

        if (openAi.TimeoutSeconds <= 0)
        {
            error = "TimeoutSeconds must be greater than zero.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateOpenAiCompatibleSettings(
        LlmProviderSettings settings,
        out string error,
        bool allowLocalhostEndpoints = false)
    {
        if (settings.OpenAiCompatible is null)
        {
            error = "OpenAI-compatible settings are required.";
            return false;
        }

        var compatible = settings.OpenAiCompatible;
        if (string.IsNullOrWhiteSpace(compatible.ApiKey))
        {
            error = "ApiKey is required.";
            return false;
        }

        if (compatible.ApiKey.Contains('\r') || compatible.ApiKey.Contains('\n'))
        {
            error = "ApiKey must not contain line breaks.";
            return false;
        }

        try
        {
            _ = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", compatible.ApiKey.Trim());
        }
        catch (FormatException)
        {
            error = "ApiKey cannot be serialized as a Bearer credential.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(compatible.Model))
        {
            error = "Model is required.";
            return false;
        }

        if (!Uri.TryCreate(compatible.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            error = "BaseUrl must be an absolute HTTP(S) URI.";
            return false;
        }

        if (!string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment) ||
            !string.IsNullOrEmpty(baseUri.UserInfo))
        {
            error = "BaseUrl must not contain user information, a query, or a fragment.";
            return false;
        }

        var ssrfResult = SsrfProtectionService.ValidateLlmProviderUrl(compatible.BaseUrl, allowLocalhostEndpoints);
        if (!ssrfResult.IsAllowed)
        {
            error = $"BaseUrl blocked by SSRF protection: {ssrfResult.ErrorMessage}";
            return false;
        }

        if (Uri.CheckHostName(baseUri.Host) != UriHostNameType.Dns)
        {
            error = "BaseUrl host must be a DNS name so it can be disclosed and enforced by the egress registry.";
            return false;
        }

        if (compatible.TimeoutSeconds is < 1 or > 300)
        {
            error = "TimeoutSeconds must be between 1 and 300.";
            return false;
        }

        if (compatible.MaxResponseBytes is < 1024 or > 4_194_304 ||
            compatible.MaxSseLineBytes is < 256 or > 262_144 ||
            compatible.MaxSseEventBytes is < 512 or > 524_288 ||
            compatible.MaxSseEventBytes > compatible.MaxResponseBytes)
        {
            error = "OpenAI-compatible response budgets are invalid or inconsistent.";
            return false;
        }

        if (compatible.ExtraHeaders is null)
        {
            error = "ExtraHeaders must be a header map when configured.";
            return false;
        }

        foreach (var (name, value) in compatible.ExtraHeaders)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                name.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("authenticate", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("X-Api-Key", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Api-Key", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Auth-Token", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Forwarded-", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("X-Taskdeck-", StringComparison.OrdinalIgnoreCase) ||
                ForbiddenCompatibleHeaders.Contains(name))
            {
                error = $"ExtraHeaders contains dangerous or reserved request header '{name}'.";
                return false;
            }

            if (value is null || value.Contains('\r') || value.Contains('\n'))
            {
                error = "ExtraHeaders values must not contain line breaks.";
                return false;
            }

            // Use the framework's request-header parser so malformed and
            // content-only/restricted names select the deterministic Mock
            // provider instead of throwing when the client constructs a request.
            using var headerProbe = new HttpRequestMessage();
            try
            {
                headerProbe.Headers.Add(name, value);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
            {
                error = $"ExtraHeaders contains an invalid or restricted request header '{name}'.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateOllamaSettings(
        LlmProviderSettings settings,
        out string error,
        bool allowLocalhostEndpoints = false)
    {
        if (settings.Ollama is null)
        {
            error = "Ollama settings are required.";
            return false;
        }

        var ollama = settings.Ollama;

        if (string.IsNullOrWhiteSpace(ollama.Model))
        {
            error = "Model is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ollama.BaseUrl))
        {
            error = "Ollama is not configured. Set Llm:Ollama:BaseUrl to the Ollama server address (e.g. http://localhost:11434).";
            return false;
        }

        if (!Uri.TryCreate(ollama.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            error = "BaseUrl must be an absolute HTTP(S) URI.";
            return false;
        }

        var ssrfResult = SsrfProtectionService.ValidateLlmProviderUrl(ollama.BaseUrl, allowLocalhostEndpoints);
        if (!ssrfResult.IsAllowed)
        {
            error = $"BaseUrl blocked by SSRF protection: {ssrfResult.ErrorMessage}";
            return false;
        }

        if (ollama.TimeoutSeconds <= 0)
        {
            error = "TimeoutSeconds must be greater than zero.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static LlmProviderKind? ResolveRequestedProviderKind(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return null;
        }

        var normalized = provider.Trim();
        if (normalized.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return LlmProviderKind.OpenAi;
        }

        if (normalized.Equals("OpenAICompatible", StringComparison.OrdinalIgnoreCase))
        {
            return LlmProviderKind.OpenAiCompatible;
        }

        if (normalized.Equals("Mock", StringComparison.OrdinalIgnoreCase))
        {
            return LlmProviderKind.Mock;
        }

        if (normalized.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            return LlmProviderKind.Ollama;
        }

        return null;
    }

    private static bool IsDevelopmentLike(string? environmentName)
    {
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            return false;
        }

        return environmentName.Equals("Development", StringComparison.OrdinalIgnoreCase) ||
               environmentName.Equals("Test", StringComparison.OrdinalIgnoreCase) ||
               environmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase);
    }
}
