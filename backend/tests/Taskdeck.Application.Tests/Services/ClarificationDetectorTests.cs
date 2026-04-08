using FluentAssertions;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ClarificationDetectorTests
{
    // ── IsClarificationResponse ─────────────────────────────────────

    [Theory]
    [InlineData("Could you tell me more about what you need?")]
    [InlineData("I can help with that! Could you tell me:\n1. How many tasks?\n2. What areas?\n3. Which column?")]
    [InlineData("Before I can create those tasks, I need to know a few things.")]
    [InlineData("To help you better, could you clarify the following?")]
    [InlineData("I'd like to ask a few questions before proceeding.")]
    public void IsClarificationResponse_ShouldReturnTrue_ForClarificationPatterns(string content)
    {
        ClarificationDetector.IsClarificationResponse(content).Should().BeTrue();
    }

    [Theory]
    [InlineData("I can help with that. I'll create a proposal to create card.")]
    [InlineData("Here's information about your request.")]
    [InlineData("Done! I've moved the card to the Done column.")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsClarificationResponse_ShouldReturnFalse_ForNonClarificationResponses(string content)
    {
        ClarificationDetector.IsClarificationResponse(content).Should().BeFalse();
    }

    [Fact]
    public void IsClarificationResponse_ShouldReturnTrue_ForMultipleQuestionMarks()
    {
        var content = "What title should the card have? And which column should it go in?";
        ClarificationDetector.IsClarificationResponse(content).Should().BeTrue();
    }

    [Fact]
    public void IsClarificationResponse_ShouldReturnFalse_ForSingleQuestionMark()
    {
        // A single question in an otherwise non-clarification response should not trigger
        var content = "I created the card. Does that look right?";
        ClarificationDetector.IsClarificationResponse(content).Should().BeFalse();
    }

    // ── IsSkipRequest ───────────────────────────────────────────────

    [Theory]
    [InlineData("just do your best")]
    [InlineData("Just do your best!")]
    [InlineData("Please just do your best")]
    [InlineData("skip clarification")]
    [InlineData("just go ahead")]
    [InlineData("go ahead")]
    [InlineData("figure it out")]
    [InlineData("just do it")]
    public void IsSkipRequest_ShouldReturnTrue_ForSkipPhrases(string message)
    {
        ClarificationDetector.IsSkipRequest(message).Should().BeTrue();
    }

    [Theory]
    [InlineData("create 3 tasks for onboarding")]
    [InlineData("put them in the Backlog column")]
    [InlineData("I want to create a card")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsSkipRequest_ShouldReturnFalse_ForNonSkipMessages(string message)
    {
        ClarificationDetector.IsSkipRequest(message).Should().BeFalse();
    }

    // ── CountClarificationRounds ────────────────────────────────────

    [Fact]
    public void CountClarificationRounds_ShouldReturn0_ForEmptyMessages()
    {
        var messages = new List<ChatMessage>();
        ClarificationDetector.CountClarificationRounds(messages).Should().Be(0);
    }

    [Fact]
    public void CountClarificationRounds_ShouldReturn0_ForNoClariMessages()
    {
        var sessionId = Guid.NewGuid();
        var messages = new List<ChatMessage>
        {
            new(sessionId, ChatMessageRole.User, "Create tasks for onboarding"),
            new(sessionId, ChatMessageRole.Assistant, "I can help with that.", "text")
        };
        ClarificationDetector.CountClarificationRounds(messages).Should().Be(0);
    }

    [Fact]
    public void CountClarificationRounds_ShouldReturn1_ForOneClarificationRound()
    {
        var sessionId = Guid.NewGuid();
        var messages = new List<ChatMessage>
        {
            new(sessionId, ChatMessageRole.User, "Create onboarding tasks"),
            new(sessionId, ChatMessageRole.Assistant, "How many tasks?", "clarification"),
            new(sessionId, ChatMessageRole.User, "3 tasks please")
        };
        ClarificationDetector.CountClarificationRounds(messages).Should().Be(1);
    }

    [Fact]
    public void CountClarificationRounds_ShouldReturn2_ForTwoClarificationRounds()
    {
        var sessionId = Guid.NewGuid();
        var messages = new List<ChatMessage>
        {
            new(sessionId, ChatMessageRole.User, "Create onboarding tasks"),
            new(sessionId, ChatMessageRole.Assistant, "How many tasks?", "clarification"),
            new(sessionId, ChatMessageRole.User, "3 tasks please"),
            new(sessionId, ChatMessageRole.Assistant, "Which column?", "clarification"),
            new(sessionId, ChatMessageRole.User, "Backlog")
        };
        ClarificationDetector.CountClarificationRounds(messages).Should().Be(2);
    }

    // ── ShouldForceBestEffort ───────────────────────────────────────

    [Fact]
    public void ShouldForceBestEffort_ShouldReturnFalse_WhenUnderMaxRounds()
    {
        var sessionId = Guid.NewGuid();
        var messages = new List<ChatMessage>
        {
            new(sessionId, ChatMessageRole.User, "Create onboarding tasks"),
            new(sessionId, ChatMessageRole.Assistant, "How many tasks?", "clarification"),
            new(sessionId, ChatMessageRole.User, "3 tasks please")
        };
        ClarificationDetector.ShouldForceBestEffort(messages).Should().BeFalse();
    }

    [Fact]
    public void ShouldForceBestEffort_ShouldReturnTrue_WhenAtMaxRounds()
    {
        var sessionId = Guid.NewGuid();
        var messages = new List<ChatMessage>
        {
            new(sessionId, ChatMessageRole.User, "Create onboarding tasks"),
            new(sessionId, ChatMessageRole.Assistant, "How many tasks?", "clarification"),
            new(sessionId, ChatMessageRole.User, "3 tasks"),
            new(sessionId, ChatMessageRole.Assistant, "Which column?", "clarification"),
            new(sessionId, ChatMessageRole.User, "Backlog")
        };
        ClarificationDetector.ShouldForceBestEffort(messages).Should().BeTrue();
    }

    // ── BuildClarificationSystemPrompt ──────────────────────────────

    [Fact]
    public void BuildClarificationSystemPrompt_ShouldIncludeAskGuidance_WhenNotForced()
    {
        var prompt = ClarificationDetector.BuildClarificationSystemPrompt(0, false);
        prompt.Should().Contain("ask clarifying questions");
        prompt.Should().Contain("0 of 2");
    }

    [Fact]
    public void BuildClarificationSystemPrompt_ShouldIncludeBestEffortDirective_WhenForced()
    {
        var prompt = ClarificationDetector.BuildClarificationSystemPrompt(2, true);
        prompt.Should().Contain("Do NOT ask any more questions");
        prompt.Should().Contain("best-effort");
    }

    // ── MaxClarificationRounds constant ─────────────────────────────

    [Fact]
    public void MaxClarificationRounds_ShouldBe2()
    {
        ClarificationDetector.MaxClarificationRounds.Should().Be(2);
    }
}
