using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Protects the application-host configuration seam for capture-triage LLM settings. Unit tests
/// already cover the extractor's disabled branch; this test proves Program binds the operator value
/// into the instance consumed by the extractor resolved from the real DI container.
/// </summary>
public sealed class CaptureTriageLlmSettingsBindingIntegrationTests
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _baseFactory;

    public CaptureTriageLlmSettingsBindingIntegrationTests(TestWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task Extractor_WhenConfigurationDisablesCaptureTriageLlm_ReturnsDisabledWithoutProviderAccess()
    {
        var provider = new Mock<ILlmProvider>(MockBehavior.Strict);
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CaptureTriageLlm:Enabled"] = "false"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILlmProvider>();
                services.AddScoped(_ => provider.Object);
            });
        });
        using var scope = factory.Services.CreateScope();

        var settings = scope.ServiceProvider.GetRequiredService<LlmCaptureTriageSettings>();
        settings.Enabled.Should().BeFalse(
            "the real application host must bind the operator's CaptureTriageLlm section");

        var extractor = scope.ServiceProvider.GetRequiredService<ILlmCaptureTriageExtractor>();
        var result = await extractor.ExtractAsync(
            Guid.NewGuid(),
            boardId: null,
            new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                CaptureSource.TranscriptPaste,
                "Alice: I will send the report by Friday."));

        result.Outcome.Should().Be(LlmCaptureTriageOutcome.Disabled);
        result.Output.Should().BeNull();
        result.Provider.Should().BeNull();
        result.Model.Should().BeNull();
        provider.VerifyNoOtherCalls();
    }
}
