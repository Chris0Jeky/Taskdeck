using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class EgressRegistryTests
{
    [Fact]
    public void DefaultRegistry_ShouldContainSeedEntries()
    {
        var registry = new EgressRegistry();
        var entries = registry.GetAllEntries();

        entries.Should().NotBeEmpty();
        entries.Count.Should().BeGreaterOrEqualTo(5, "seed entries cover OpenAI, Gemini, webhooks, Sentry, analytics");
    }

    [Fact]
    public void DefaultRegistry_ShouldAllowOpenAiHost()
    {
        var registry = new EgressRegistry();
        registry.IsHostAllowed("api.openai.com").Should().BeTrue();
    }

    [Fact]
    public void DefaultRegistry_ShouldAllowGeminiHost()
    {
        var registry = new EgressRegistry();
        registry.IsHostAllowed("generativelanguage.googleapis.com").Should().BeTrue();
    }

    [Fact]
    public void DefaultRegistry_ShouldRejectUnknownHost()
    {
        var registry = new EgressRegistry();
        registry.IsHostAllowed("evil-exfiltration.example.com").Should().BeFalse();
    }

    [Fact]
    public void IsHostAllowed_ShouldBeCaseInsensitive()
    {
        var registry = new EgressRegistry();
        registry.IsHostAllowed("API.OPENAI.COM").Should().BeTrue();
        registry.IsHostAllowed("Api.OpenAi.Com").Should().BeTrue();
    }

    [Fact]
    public void IsHostAllowed_ShouldHandleTrailingDot()
    {
        var registry = new EgressRegistry();
        registry.IsHostAllowed("api.openai.com.").Should().BeTrue();
    }

    [Fact]
    public void IsHostAllowed_ShouldRejectEmptyString()
    {
        var registry = new EgressRegistry();
        registry.IsHostAllowed("").Should().BeFalse();
    }

    [Fact]
    public void IsHostAllowed_ShouldRejectWhitespace()
    {
        var registry = new EgressRegistry();
        registry.IsHostAllowed("   ").Should().BeFalse();
    }

    [Fact]
    public void Register_ShouldAddNewEntry()
    {
        var registry = new EgressRegistry();
        var initialCount = registry.GetAllEntries().Count;

        registry.Register(new EgressEntry(
            Host: "custom-webhook.example.com",
            PayloadCategory: "Custom notification",
            ToolOrAgentName: "CustomConnector",
            Classification: EgressDataClassification.MetadataOnly));

        registry.GetAllEntries().Count.Should().Be(initialCount + 1);
        registry.IsHostAllowed("custom-webhook.example.com").Should().BeTrue();
    }

    [Fact]
    public void Register_ShouldThrowOnNull()
    {
        var registry = new EgressRegistry();
        var act = () => registry.Register(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SeedEntries_ShouldHaveCorrectClassifications()
    {
        var registry = new EgressRegistry();
        var entries = registry.GetAllEntries();

        // LLM providers carry UserContent
        var openAiEntry = entries.First(e => e.Host == "api.openai.com");
        openAiEntry.Classification.Should().Be(EgressDataClassification.UserContent);

        var geminiEntry = entries.First(e => e.Host == "generativelanguage.googleapis.com");
        geminiEntry.Classification.Should().Be(EgressDataClassification.UserContent);
    }

    [Fact]
    public void SeedEntries_ShouldDocumentAllKnownProviders()
    {
        var registry = new EgressRegistry();
        var entries = registry.GetAllEntries();
        var hosts = entries.Select(e => e.Host).ToList();

        // Verify all known outbound paths are documented
        hosts.Should().Contain("api.openai.com", "OpenAI provider must be registered");
        hosts.Should().Contain("generativelanguage.googleapis.com", "Gemini provider must be registered");
    }

    [Fact]
    public void CustomRegistry_ShouldOnlyAllowProvidedEntries()
    {
        var entries = new[]
        {
            new EgressEntry("only-this-host.com", "Test", "Test", EgressDataClassification.None),
        };

        var registry = new EgressRegistry(entries);

        registry.IsHostAllowed("only-this-host.com").Should().BeTrue();
        registry.IsHostAllowed("api.openai.com").Should().BeFalse();
    }

    [Fact]
    public void EgressDataClassification_ShouldHaveExpectedValues()
    {
        // Verify the enum covers all expected classifications
        Enum.GetValues<EgressDataClassification>().Should().HaveCount(4);
        ((int)EgressDataClassification.None).Should().Be(0);
        ((int)EgressDataClassification.MetadataOnly).Should().Be(1);
        ((int)EgressDataClassification.UserContent).Should().Be(2);
        ((int)EgressDataClassification.Credentials).Should().Be(3);
    }

    [Fact]
    public void EgressEntry_ShouldBeImmutableRecord()
    {
        var entry = new EgressEntry("host.com", "payload", "tool", EgressDataClassification.None);
        entry.Host.Should().Be("host.com");
        entry.PayloadCategory.Should().Be("payload");
        entry.ToolOrAgentName.Should().Be("tool");
        entry.Classification.Should().Be(EgressDataClassification.None);
    }
}
