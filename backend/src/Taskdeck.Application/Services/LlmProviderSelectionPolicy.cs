namespace Taskdeck.Application.Services;

public enum LlmProviderKind
{
    Mock = 0,
    OpenAi = 1
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

        if (!IsOpenAiMode(settings.Provider))
        {
            return new LlmProviderDecision(
                LlmProviderKind.Mock,
                $"Provider mode '{settings.Provider}' resolves to mock.");
        }

        if (IsDevelopmentLike(environmentName) && !settings.AllowLiveProvidersInDevelopment)
        {
            return new LlmProviderDecision(
                LlmProviderKind.Mock,
                "Live providers are disabled for development-like environments.");
        }

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

    public static bool TryValidateOpenAiSettings(LlmProviderSettings settings, out string error)
    {
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

    private static bool IsOpenAiMode(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return false;
        }

        return provider.Trim().Equals("OpenAI", StringComparison.OrdinalIgnoreCase);
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
