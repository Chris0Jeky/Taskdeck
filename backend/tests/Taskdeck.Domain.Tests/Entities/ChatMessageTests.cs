using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ChatMessageTests
{
    [Fact]
    public void Constructor_ShouldStoreDegradedReason_ForDegradedMessages()
    {
        var sessionId = Guid.NewGuid();

        var message = new ChatMessage(
            sessionId,
            ChatMessageRole.Assistant,
            "Fallback response",
            messageType: "degraded",
            tokenUsage: 12,
            degradedReason: "Live provider request failed.");

        message.MessageType.Should().Be("degraded");
        message.DegradedReason.Should().Be("Live provider request failed.");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t\r\n  ")]
    public void Constructor_ShouldThrow_WhenContentIsEmpty(string content)
    {
        // The empty-content guard is what forces callers to substitute an explicit placeholder
        // when a provider returns no assistant text (#1617) — an assistant-history row must
        // never be blank, so this rejection is load-bearing, not incidental.
        var sessionId = Guid.NewGuid();

        var act = () => new ChatMessage(
            sessionId,
            ChatMessageRole.Assistant,
            content);

        act.Should().Throw<DomainException>()
            .WithMessage("Content cannot be empty")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenMessageTypeIsNotAllowed()
    {
        var sessionId = Guid.NewGuid();

        var act = () => new ChatMessage(
            sessionId,
            ChatMessageRole.Assistant,
            "Fallback response",
            messageType: "custom");

        act.Should().Throw<DomainException>()
            .WithMessage("MessageType must be one of: text, proposal-reference, error, status, degraded, clarification, parse-hint")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void SetToolCallMetadataJson_ShouldStoreMetadata()
    {
        var message = new ChatMessage(
            Guid.NewGuid(),
            ChatMessageRole.Assistant,
            "I created a proposal.");

        message.ToolCallMetadataJson.Should().BeNull();

        message.SetToolCallMetadataJson("{\"rounds\":2,\"tool_calls\":[]}");

        message.ToolCallMetadataJson.Should().Be("{\"rounds\":2,\"tool_calls\":[]}");
    }

    [Fact]
    public void SetToolCallMetadataJson_NullOrEmpty_ClearsMetadata()
    {
        var message = new ChatMessage(
            Guid.NewGuid(),
            ChatMessageRole.Assistant,
            "Some response.");

        message.SetToolCallMetadataJson("{\"rounds\":1}");
        message.ToolCallMetadataJson.Should().NotBeNull();

        message.SetToolCallMetadataJson(null);
        message.ToolCallMetadataJson.Should().BeNull();

        message.SetToolCallMetadataJson("  ");
        message.ToolCallMetadataJson.Should().BeNull();
    }
}
