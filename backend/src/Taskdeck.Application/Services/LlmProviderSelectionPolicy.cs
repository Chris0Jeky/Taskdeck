namespace Taskdeck.Application.Services;

public enum LlmProviderKind
{
    Mock = 0,
    OpenAi = 1,
    Gemini = 2
}

public sealed record LlmProviderDecision(LlmProviderKind ProviderKind, string Reason);

public static class LlmProviderSelectionPolicy
{
    public static LlmProviderDecision Evaluate(LlmProviderSettings settings, string? environmentName)
    {
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

        var requestedProvider = ResolveRequestedProviderKind(settings.Provider);
        if (!requestedProvider.HasValue)
        {
            return new LlmProviderDecision(
                LlmProviderKind.Mock,
                $"Provider mode '{settings.Provider}' resolves to mock.");
        }

        if (requestedProvider.Value == LlmProviderKind.OpenAi)
        {
            if (!TryValidateOpenAiSettings(settings, out var validationError))
            {
                return new LlmProviderDecision(
                    LlmProviderKind.Mock,
                    $"OpenAI configuration is invalid: {validationError}");
            }

            return new LlmProviderDecision(
                LlmProviderKind.OpenAi,
                "OpenAI provider selected.");
        }

        if (!TryValidateGeminiSettings(settings, out var geminiValidationError))
        {
            return new LlmProviderDecision(
                LlmProviderKind.Mock,
                $"Gemini configuration is invalid: {geminiValidationError}");
        }

        return new LlmProviderDecision(
            LlmProviderKind.Gemini,
            "Gemini provider selected.");
    }

    public static bool TryValidateOpenAiSettings(LlmProviderSettings settings, out string error)
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

        if (openAi.TimeoutSeconds <= 0)
        {
            error = "TimeoutSeconds must be greater than zero.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateGeminiSettings(LlmProviderSettings settings, out string error)
    {
        if (settings.Gemini is null)
        {
            error = "Gemini settings are required.";
            return false;
        }

        var gemini = settings.Gemini;

        if (string.IsNullOrWhiteSpace(gemini.ApiKey))
        {
            error = "ApiKey is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(gemini.Model))
        {
            error = "Model is required.";
            return false;
        }

        if (!Uri.TryCreate(gemini.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            error = "BaseUrl must be an absolute HTTP(S) URI.";
            return false;
        }

        if (gemini.TimeoutSeconds <= 0)
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

        if (normalized.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            return LlmProviderKind.Gemini;
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
