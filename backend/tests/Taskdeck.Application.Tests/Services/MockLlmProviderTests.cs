using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class MockLlmProviderTests
{
    [Fact]
    public async Task CompleteAsync_ShouldTreatUserRoleCaseInsensitively()
    {
        var provider = new MockLlmProvider();

        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            new List<ChatCompletionMessage>
            {
                new("user", "create card for triage queue")
            }));

        result.IsActionable.Should().BeTrue();
        result.ActionIntent.Should().Be("card.create");
        result.Provider.Should().Be("Mock");
        result.Model.Should().Be("mock-default");
    }

    [Fact]
    public async Task GetHealthAsync_ShouldReportMockProviderMetadata()
    {
        var provider = new MockLlmProvider();

        var health = await provider.GetHealthAsync();

        health.IsAvailable.Should().BeTrue();
        health.ProviderName.Should().Be("Mock");
        health.Model.Should().Be("mock-default");
    }
}
