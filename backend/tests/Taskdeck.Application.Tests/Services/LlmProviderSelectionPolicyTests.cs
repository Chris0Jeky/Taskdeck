using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LlmProviderSelectionPolicyTests
{
    [Fact]
    public void Evaluate_ShouldSelectMock_WhenLiveProvidersDisabled()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = false;
        settings.Provider = "OpenAI";

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("disabled");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenDevelopmentEnvironmentDisallowsLiveProviders()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.AllowLiveProvidersInDevelopment = false;
        settings.Provider = "OpenAI";

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Development");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("development-like");
    }

    [Theory]
    [InlineData("Test")]
    [InlineData("Testing")]
    public void Evaluate_ShouldSelectMock_WhenTestLikeEnvironmentDisallowsLiveProviders(string environmentName)
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.AllowLiveProvidersInDevelopment = false;
        settings.Provider = "OpenAI";

        var result = LlmProviderSelectionPolicy.Evaluate(settings, environmentName);

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("development-like");
    }

    [Fact]
    public void Evaluate_ShouldSelectOpenAi_WhenDevelopmentEnvironmentAllowsLiveProviders()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.AllowLiveProvidersInDevelopment = true;
        settings.Provider = "OpenAI";

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Development");

        result.ProviderKind.Should().Be(LlmProviderKind.OpenAi);
    }

    [Fact]
    public void Evaluate_ShouldSelectOpenAi_WhenProductionAndConfigurationIsValid()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.AllowLiveProvidersInDevelopment = false;
        settings.Provider = "OpenAI";

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.OpenAi);
    }

    [Fact]
    public void Evaluate_ShouldSelectGemini_WhenProductionAndConfigurationIsValid()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.AllowLiveProvidersInDevelopment = false;
        settings.Provider = "Gemini";

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Gemini);
    }

    [Fact]
    public void Evaluate_ShouldSelectOpenAiCompatible_WhenProductionAndConfigurationIsValid()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "OpenAICompatible";
        settings.OpenAiCompatible = new OpenAiCompatibleProviderSettings
        {
            ApiKey = "test-compatible-key",
            BaseUrl = "https://api.groq.com/openai/v1",
            Model = "llama-3.1-8b-instant",
            ExtraHeaders = new Dictionary<string, string> { ["X-Title"] = "Taskdeck" }
        };

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.OpenAiCompatible);
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenOpenAiCompatibleBaseUrlTargetsPrivateHost()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "OpenAICompatible";
        settings.OpenAiCompatible = new OpenAiCompatibleProviderSettings
        {
            ApiKey = "test-compatible-key",
            BaseUrl = "https://127.0.0.1/v1",
            Model = "local-model"
        };

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("SSRF");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenOpenAiCompatibleExtraHeadersIsNull()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "OpenAICompatible";
        settings.OpenAiCompatible = new OpenAiCompatibleProviderSettings
        {
            ApiKey = "test-compatible-key",
            BaseUrl = "https://api.groq.com/openai/v1",
            Model = "llama-3.1-8b-instant",
            ExtraHeaders = null!
        };

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("ExtraHeaders");
    }

    [Theory]
    [InlineData("Content-Type")]
    [InlineData("X Invalid")]
    public void Evaluate_ShouldSelectMock_WhenOpenAiCompatibleExtraHeaderIsInvalidOrRestricted(string headerName)
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "OpenAICompatible";
        settings.OpenAiCompatible = new OpenAiCompatibleProviderSettings
        {
            ApiKey = "test-compatible-key",
            BaseUrl = "https://api.groq.com/openai/v1",
            Model = "llama-3.1-8b-instant",
            ExtraHeaders = new Dictionary<string, string> { [headerName] = "value" }
        };

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("invalid or restricted");
    }

    [Theory]
    [InlineData("Host")]
    [InlineData("Proxy-Authorization")]
    [InlineData("Connection")]
    [InlineData("Transfer-Encoding")]
    [InlineData("Cookie")]
    [InlineData("Set-Cookie")]
    [InlineData("X-Authorization")]
    [InlineData("Authentication-Info")]
    [InlineData("WWW-Authenticate")]
    [InlineData("X-Api-Key")]
    [InlineData("X-Auth-Token")]
    [InlineData("X-Forwarded-Host")]
    [InlineData("X-Taskdeck-Correlation-Id")]
    public void Evaluate_ShouldSelectMock_WhenOpenAiCompatibleExtraHeaderIsDangerous(string headerName)
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "OpenAICompatible";
        settings.OpenAiCompatible = new OpenAiCompatibleProviderSettings
        {
            ApiKey = "test-compatible-key",
            BaseUrl = "https://api.groq.com/openai/v1",
            Model = "llama-3.1-8b-instant",
            ExtraHeaders = new Dictionary<string, string> { [headerName] = "value" }
        };

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("dangerous or reserved");
    }

    [Theory]
    [InlineData("test\r\nX-Evil: yes", "ApiKey", null)]
    [InlineData("test-compatible-key", "BaseUrl", "https://api.groq.com/openai/v1?target=other")]
    [InlineData("test-compatible-key", "BaseUrl", "https://api.groq.com/openai/v1#fragment")]
    public void Evaluate_ShouldSelectMock_WhenOpenAiCompatibleCredentialOrBaseUrlIsAmbiguous(
        string apiKey,
        string expectedReason,
        string? baseUrl = null)
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "OpenAICompatible";
        settings.OpenAiCompatible = new OpenAiCompatibleProviderSettings
        {
            ApiKey = apiKey,
            BaseUrl = baseUrl ?? "https://api.groq.com/openai/v1",
            Model = "llama-3.1-8b-instant"
        };

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain(expectedReason);
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenOpenAiCompatibleTimeoutExceedsDeclaredMaximum()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "OpenAICompatible";
        settings.OpenAiCompatible = new OpenAiCompatibleProviderSettings
        {
            ApiKey = "test-compatible-key",
            BaseUrl = "https://api.groq.com/openai/v1",
            Model = "llama-3.1-8b-instant",
            TimeoutSeconds = 301
        };

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("between 1 and 300");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenOpenAiCompatibleResponseBudgetsAreInconsistent()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "OpenAICompatible";
        settings.OpenAiCompatible = new OpenAiCompatibleProviderSettings
        {
            ApiKey = "test-compatible-key",
            BaseUrl = "https://api.groq.com/openai/v1",
            Model = "llama-3.1-8b-instant",
            MaxResponseBytes = 1024,
            MaxSseLineBytes = 512,
            MaxSseEventBytes = 2048
        };

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("budgets");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenGeminiConfigurationIsInvalid()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "Gemini";
        settings.Gemini.ApiKey = string.Empty;

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("Gemini configuration is invalid");
        result.Reason.Should().Contain("ApiKey is required");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenGeminiBaseUrlIsInvalid()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "Gemini";
        settings.Gemini.BaseUrl = "not-a-url";

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("BaseUrl must be an absolute HTTP(S) URI");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenGeminiTimeoutIsNotPositive()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "Gemini";
        settings.Gemini.TimeoutSeconds = 0;

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("TimeoutSeconds must be greater than zero");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenGeminiSettingsAreMissing()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "Gemini";
        settings.Gemini = null!;

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("Gemini settings are required");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenOpenAiConfigurationIsInvalid()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "OpenAI";
        settings.OpenAi.ApiKey = string.Empty;

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("invalid");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenOpenAiModelIsMissing()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "OpenAI";
        settings.OpenAi.Model = string.Empty;

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("Model is required");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenOpenAiBaseUrlIsInvalid()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "OpenAI";
        settings.OpenAi.BaseUrl = "not-a-url";

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("BaseUrl must be an absolute HTTP(S) URI");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenOpenAiTimeoutIsNotPositive()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "OpenAI";
        settings.OpenAi.TimeoutSeconds = 0;

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("TimeoutSeconds must be greater than zero");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenOpenAiSettingsAreMissing()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "OpenAI";
        settings.OpenAi = null!;

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("OpenAI settings are required");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenProviderModeIsUnknown()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "OtherProvider";

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Contain("resolves to mock");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenProviderModeIsExplicitMock_AndLiveProvidersAreDisabled()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = false;
        settings.AllowLiveProvidersInDevelopment = false;
        settings.Provider = "Mock";

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Development");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock);
        result.Reason.Should().Be("Mock provider selected.");
    }

    // -----------------------------------------------------------------------
    // SSRF protection for LLM provider BaseUrl (SEC-26)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("https://10.0.0.1/v1")]
    [InlineData("https://192.168.1.1/v1")]
    [InlineData("https://172.16.0.1/v1")]
    [InlineData("https://127.0.0.1/v1")]
    [InlineData("https://[::1]/v1")]
    [InlineData("https://metadata.google.internal/v1")]
    [InlineData("https://metadata.goog/v1")]
    public void Evaluate_ShouldSelectMock_WhenOpenAiBaseUrlTargetsPrivateOrInternalHost(string baseUrl)
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "OpenAI";
        settings.OpenAi.BaseUrl = baseUrl;

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock,
            $"OpenAI BaseUrl '{baseUrl}' targets a private/internal host and should be blocked by SSRF protection");
        result.Reason.Should().Contain("SSRF");
    }

    [Theory]
    [InlineData("https://10.0.0.1/v1beta")]
    [InlineData("https://192.168.1.1/v1beta")]
    [InlineData("https://172.16.0.1/v1beta")]
    [InlineData("https://127.0.0.1/v1beta")]
    [InlineData("https://[::1]/v1beta")]
    [InlineData("https://metadata.google.internal/v1beta")]
    [InlineData("https://metadata.goog/v1beta")]
    public void Evaluate_ShouldSelectMock_WhenGeminiBaseUrlTargetsPrivateOrInternalHost(string baseUrl)
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "Gemini";
        settings.Gemini.BaseUrl = baseUrl;

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock,
            $"Gemini BaseUrl '{baseUrl}' targets a private/internal host and should be blocked by SSRF protection");
        result.Reason.Should().Contain("SSRF");
    }

    [Theory]
    [InlineData("http://api.openai.com/v1")]
    public void Evaluate_ShouldSelectMock_WhenOpenAiBaseUrlUsesHttp(string baseUrl)
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "OpenAI";
        settings.OpenAi.BaseUrl = baseUrl;

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock,
            $"LLM provider BaseUrl '{baseUrl}' uses HTTP, which is not allowed");
        result.Reason.Should().Contain("SSRF");
    }

    [Theory]
    [InlineData("https://[::ffff:10.0.0.1]/v1")]
    [InlineData("https://[::ffff:192.168.1.1]/v1")]
    public void Evaluate_ShouldSelectMock_WhenOpenAiBaseUrlUsesIpv4MappedIpv6(string baseUrl)
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.Provider = "OpenAI";
        settings.OpenAi.BaseUrl = baseUrl;

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock,
            $"OpenAI BaseUrl '{baseUrl}' uses IPv4-mapped IPv6 to target private addresses");
        result.Reason.Should().Contain("SSRF");
    }

    [Fact]
    public void TryValidateOpenAiSettings_ShouldRejectPrivateIpBaseUrl()
    {
        var settings = BuildValidSettings();
        settings.OpenAi.BaseUrl = "https://10.0.0.5/v1";

        var isValid = LlmProviderSelectionPolicy.TryValidateOpenAiSettings(settings, out var error);

        isValid.Should().BeFalse();
        error.Should().Contain("SSRF");
    }

    [Fact]
    public void TryValidateGeminiSettings_ShouldRejectPrivateIpBaseUrl()
    {
        var settings = BuildValidSettings();
        settings.Gemini.BaseUrl = "https://192.168.1.1/v1beta";

        var isValid = LlmProviderSelectionPolicy.TryValidateGeminiSettings(settings, out var error);

        isValid.Should().BeFalse();
        error.Should().Contain("SSRF");
    }

    [Fact]
    public void TryValidateOpenAiSettings_ShouldAcceptLegitimateBaseUrl()
    {
        var settings = BuildValidSettings();
        settings.OpenAi.BaseUrl = "https://api.openai.com/v1";

        var isValid = LlmProviderSelectionPolicy.TryValidateOpenAiSettings(settings, out _);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void TryValidateGeminiSettings_ShouldAcceptLegitimateBaseUrl()
    {
        var settings = BuildValidSettings();
        settings.Gemini.BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

        var isValid = LlmProviderSelectionPolicy.TryValidateGeminiSettings(settings, out _);

        isValid.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Development localhost bypass — local LLM gateways (Ollama, LM Studio)
    // -----------------------------------------------------------------------

    [Fact]
    public void Evaluate_ShouldSelectOpenAi_WhenLocalhostInDevelopmentWithAllowLiveProviders()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.AllowLiveProvidersInDevelopment = true;
        settings.Provider = "OpenAI";
        settings.OpenAi.BaseUrl = "http://localhost:11434/v1";

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Development");

        result.ProviderKind.Should().Be(LlmProviderKind.OpenAi,
            "localhost should be allowed in development with AllowLiveProvidersInDevelopment for local LLM gateways like Ollama");
    }

    [Fact]
    public void Evaluate_ShouldSelectGemini_WhenLocalhostInDevelopmentWithAllowLiveProviders()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.AllowLiveProvidersInDevelopment = true;
        settings.Provider = "Gemini";
        settings.Gemini.BaseUrl = "http://localhost:8080/v1beta";

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Development");

        result.ProviderKind.Should().Be(LlmProviderKind.Gemini,
            "localhost should be allowed in development with AllowLiveProvidersInDevelopment");
    }

    [Fact]
    public void Evaluate_ShouldSelectMock_WhenLocalhostInProductionEvenWithAllowLiveProviders()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.AllowLiveProvidersInDevelopment = true;
        settings.Provider = "OpenAI";
        settings.OpenAi.BaseUrl = "http://localhost:11434/v1";

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Production");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock,
            "localhost should still be blocked in production even if AllowLiveProvidersInDevelopment is set");
        result.Reason.Should().Contain("SSRF");
    }

    [Fact]
    public void TryValidateOpenAiSettings_ShouldAcceptLocalhostWhenAllowed()
    {
        var settings = BuildValidSettings();
        settings.OpenAi.BaseUrl = "http://localhost:11434/v1";

        var isValid = LlmProviderSelectionPolicy.TryValidateOpenAiSettings(
            settings, out _, allowLocalhostEndpoints: true);

        isValid.Should().BeTrue(
            "localhost should be accepted when allowLocalhostEndpoints is true (development mode)");
    }

    [Fact]
    public void TryValidateOpenAiSettings_ShouldRejectLocalhostWhenNotAllowed()
    {
        var settings = BuildValidSettings();
        settings.OpenAi.BaseUrl = "http://localhost:11434/v1";

        var isValid = LlmProviderSelectionPolicy.TryValidateOpenAiSettings(
            settings, out var error, allowLocalhostEndpoints: false);

        isValid.Should().BeFalse(
            "localhost should be rejected when allowLocalhostEndpoints is false (production mode)");
        error.Should().Contain("SSRF");
    }

    [Fact]
    public void Evaluate_ShouldStillBlockPrivateIps_InDevelopmentWithAllowLiveProviders()
    {
        var settings = BuildValidSettings();
        settings.EnableLiveProviders = true;
        settings.AllowLiveProvidersInDevelopment = true;
        settings.Provider = "OpenAI";
        settings.OpenAi.BaseUrl = "https://10.0.0.1/v1";

        var result = LlmProviderSelectionPolicy.Evaluate(settings, "Development");

        result.ProviderKind.Should().Be(LlmProviderKind.Mock,
            "private IPs (not localhost) should still be blocked even in development mode");
        result.Reason.Should().Contain("SSRF");
    }

    private static LlmProviderSettings BuildValidSettings()
    {
        return new LlmProviderSettings
        {
            EnableLiveProviders = true,
            Provider = "OpenAI",
            OpenAi = new OpenAiProviderSettings
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.openai.com/v1",
                Model = "gpt-4o-mini",
                TimeoutSeconds = 30
            },
            Gemini = new GeminiProviderSettings
            {
                ApiKey = "test-gemini-key",
                BaseUrl = "https://generativelanguage.googleapis.com/v1beta",
                Model = "gemini-2.5-flash",
                TimeoutSeconds = 30
            }
        };
    }
}
