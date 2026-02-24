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
