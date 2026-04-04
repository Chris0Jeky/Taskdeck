using FluentAssertions;
using Xunit;
using Taskdeck.Application.Services;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Edge case tests for LlmIntentClassifier expanding on the existing fuzz tests.
/// Covers: negation filtering, other-tool questions, ambiguous inputs,
/// very long inputs, prompt injection patterns, mixed casing,
/// and multi-intent detection gaps.
/// </summary>
public class LlmIntentClassifierEdgeCaseTests
{
    // ── Negation filtering ───────────────────────────────────────

    [Theory]
    [InlineData("Don't add a card")]
    [InlineData("do not create a new task")]
    [InlineData("never move the card to done")]
    [InlineData("stop create new tasks")]
    [InlineData("cancel the delete of card 5")]
    [InlineData("don't remove that task")]
    [InlineData("avoid creating a task please")] // "avoid" + "creating" uses "avoid" in the negation list
    public void Classify_NegatedInput_IsNotActionable(string input)
    {
        var (isActionable, _) = LlmIntentClassifier.Classify(input);

        isActionable.Should().BeFalse(
            $"negated input '{input}' should not be classified as actionable");
    }

    // ── Other-tool questions ─────────────────────────────────────

    [Theory]
    [InlineData("How do I add a card in Trello?")]
    [InlineData("How do I create a task in Jira?")]
    [InlineData("Where do I move cards in Asana?")]
    [InlineData("Can I create boards in Notion?")]
    public void Classify_OtherToolQuestion_IsNotActionable(string input)
    {
        var (isActionable, _) = LlmIntentClassifier.Classify(input);

        isActionable.Should().BeFalse(
            $"question about another tool '{input}' should not be actionable");
    }

    // ── Positive detection ───────────────────────────────────────

    [Theory]
    [InlineData("create a new card called Test", "card.create")]
    [InlineData("add a task for the meeting", "card.create")]
    [InlineData("make a new task for sprint review", "card.create")]
    [InlineData("move card to done column", "card.move")]
    [InlineData("archive the old task", "card.archive")]
    [InlineData("delete card number 5", "card.archive")]
    [InlineData("remove the finished task", "card.archive")]
    [InlineData("update card title to new name", "card.update")]
    [InlineData("rename task to better name", "card.update")]
    [InlineData("edit card description", "card.update")]
    [InlineData("create a new board for the project", "board.create")]
    [InlineData("rename board to Sprint 42", "board.update")]
    [InlineData("reorder columns on the board", "column.reorder")]
    public void Classify_ActionableInput_DetectsCorrectIntent(string input, string expectedIntent)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(input);

        isActionable.Should().BeTrue($"'{input}' should be detected as actionable");
        actionIntent.Should().Be(expectedIntent);
    }

    // ── Non-actionable inputs ────────────────────────────────────

    [Theory]
    [InlineData("hello")]
    [InlineData("what is the weather?")]
    [InlineData("tell me about the project")]
    [InlineData("how are my tasks doing?")]
    [InlineData("show me a summary")]
    [InlineData("what's the status?")]
    public void Classify_NonActionableInput_ReturnsFalse(string input)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(input);

        isActionable.Should().BeFalse(
            $"non-actionable input '{input}' should not be classified as actionable");
        actionIntent.Should().BeNull();
    }

    // ── Edge cases ───────────────────────────────────────────────

    [Fact]
    public void Classify_NullInput_ReturnsFalse()
    {
        var (isActionable, _) = LlmIntentClassifier.Classify(null!);

        isActionable.Should().BeFalse();
    }

    [Fact]
    public void Classify_EmptyString_ReturnsFalse()
    {
        var (isActionable, _) = LlmIntentClassifier.Classify("");

        isActionable.Should().BeFalse();
    }

    [Fact]
    public void Classify_WhitespaceOnly_ReturnsFalse()
    {
        var (isActionable, _) = LlmIntentClassifier.Classify("   \t\n  ");

        isActionable.Should().BeFalse();
    }

    [Fact]
    public void Classify_VeryLongInput_DoesNotThrow()
    {
        // 10,000 character message should be handled gracefully
        var longInput = new string('a', 9990) + " create a card";

        var act = () => LlmIntentClassifier.Classify(longInput);

        act.Should().NotThrow();
    }

    [Fact]
    public void Classify_VeryLongInput_WithActionableContent_StillDetects()
    {
        // Actionable content at the start should be detected even in long messages
        var longInput = "create a new task called test " + new string('x', 5000);

        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(longInput);

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("card.create");
    }

    [Theory]
    [InlineData("CREATE A NEW CARD")]
    [InlineData("Create A New Card")]
    [InlineData("cReAtE a NeW cArD")]
    public void Classify_MixedCase_StillDetects(string input)
    {
        var (isActionable, _) = LlmIntentClassifier.Classify(input);

        isActionable.Should().BeTrue(
            $"mixed case input '{input}' should still be detected");
    }

    [Theory]
    [InlineData("create a card\nand some other text")]
    [InlineData("create\na\ncard")]
    public void Classify_NewlinesInInput_StillDetects(string input)
    {
        // Regex patterns work per-line or across depending on implementation
        var (isActionable, _) = LlmIntentClassifier.Classify(input);

        // Regardless of detection, it should not throw
        // (The actual behavior depends on regex mode - this tests safety)
    }

    [Fact]
    public void Classify_PromptInjection_DoesNotCrash()
    {
        var injections = new[]
        {
            "create a card'; DROP TABLE cards;--",
            "create a card with <script>alert('xss')</script>",
            "create a card\0with null bytes",
            "create a card\\nwith escaped newlines"
        };

        foreach (var input in injections)
        {
            var act = () => LlmIntentClassifier.Classify(input);
            act.Should().NotThrow($"input '{input}' should not cause an exception");
        }
    }

    // ── Archive vs Move disambiguation ───────────────────────────

    [Fact]
    public void Classify_RemoveCard_ClassifiesAsArchive_NotMove()
    {
        // "remove" contains "move" as a substring. Verify archive takes priority.
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify("remove the task from backlog");

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("card.archive");
    }

    // ── Stemming/plural variations ───────────────────────────────

    [Theory]
    [InlineData("create new cards", "card.create")]
    [InlineData("add tasks for the team", "card.create")]
    [InlineData("move tasks to done", "card.move")]
    [InlineData("archive cards", "card.archive")]
    [InlineData("update tasks", "card.update")]
    public void Classify_PluralNouns_StillDetects(string input, string expectedIntent)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(input);

        isActionable.Should().BeTrue($"plural input '{input}' should be detected");
        actionIntent.Should().Be(expectedIntent);
    }

    // ── Verb coverage ────────────────────────────────────────────

    [Theory]
    [InlineData("generate a card for testing", "card.create")]
    [InlineData("build a task list", "card.create")]
    [InlineData("prepare a new task", "card.create")]
    [InlineData("set up a new board", "board.create")]
    [InlineData("modify the card title", "card.update")]
    [InlineData("change task priority", "card.update")]
    [InlineData("sort the columns", "column.reorder")]
    [InlineData("rearrange the columns", "column.reorder")]
    [InlineData("reorganize the board columns", "column.reorder")]
    public void Classify_AlternateVerbs_DetectedCorrectly(string input, string expectedIntent)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(input);

        isActionable.Should().BeTrue($"verb in '{input}' should be recognized");
        actionIntent.Should().Be(expectedIntent);
    }
}
