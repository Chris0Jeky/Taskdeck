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
            }
        };
    }
}
