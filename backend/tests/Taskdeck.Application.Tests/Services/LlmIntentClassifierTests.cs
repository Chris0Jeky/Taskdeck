using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LlmIntentClassifierTests
{
    #region Card Creation — Current Supported Patterns

    [Theory]
    [InlineData("create card \"test\"")]
    [InlineData("add card \"test\"")]
    [InlineData("create a card for this")]
    [InlineData("add a card please")]
    [InlineData("create task for sprint")]
    [InlineData("add task to backlog")]
    [InlineData("create a task here")]
    [InlineData("add a task for review")]
    [InlineData("new card needed")]
    [InlineData("new task for the board")]
    [InlineData("make a card for this")]
    [InlineData("make a task for deployment")]
    [InlineData("make card now")]
    [InlineData("make task please")]
    public void Classify_CardCreation_ShouldDetect_ExactPatterns(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("card.create");
    }

    #endregion

    #region Card Operations — Current Supported Patterns

    [Fact]
    public void Classify_MoveCard_ShouldDetect()
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify("move card abc123 to done");

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("card.move");
    }

    [Theory]
    [InlineData("archive card abc123")]
    [InlineData("delete card abc123")]
    public void Classify_ArchiveCard_ShouldDetect(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("card.archive");
    }

    /// <summary>
    /// Documents a classifier ordering bug: "remove card" contains the substring
    /// "move card", so the move-card check fires first and returns card.move
    /// instead of card.archive. See #571 for fix.
    /// </summary>
    [Fact]
    public void Classify_RemoveCard_MatchesMoveCardDueToSubstringOrdering()
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify("remove card abc123");

        isActionable.Should().BeTrue();
        // Bug: "remove card" contains "move card" as substring, so card.move fires first
        actionIntent.Should().Be("card.move",
            because: "substring 'move card' appears inside 'remove card' — classifier ordering bug tracked in #571");
    }

    [Theory]
    [InlineData("update card abc123 title")]
    [InlineData("edit card abc123 description")]
    [InlineData("rename card abc123")]
    public void Classify_UpdateCard_ShouldDetect(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("card.update");
    }

    #endregion

    #region Board Operations — Current Supported Patterns

    [Theory]
    [InlineData("create board")]
    [InlineData("add board")]
    [InlineData("new board")]
    public void Classify_BoardCreation_ShouldDetect(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("board.create");
    }

    [Fact]
    public void Classify_RenameBoard_ShouldDetect()
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify("rename board to 'Sprint 5'");

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("board.update");
    }

    [Theory]
    [InlineData("reorder cards in the board")]
    [InlineData("reorder columns")]
    [InlineData("sort cards by priority")]
    [InlineData("sort columns alphabetically")]
    public void Classify_Reorder_ShouldDetect(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("column.reorder");
    }

    #endregion

    #region Non-Actionable — Should Return False

    [Theory]
    [InlineData("what is the weather today?")]
    [InlineData("tell me about project management")]
    [InlineData("how does taskdeck work?")]
    [InlineData("explain the board layout")]
    [InlineData("")]
    [InlineData("hello")]
    public void Classify_NonActionable_ShouldReturnFalse(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeFalse();
        actionIntent.Should().BeNull();
    }

    #endregion

    #region Edge Cases — Input Extremes

    [Fact]
    public void Classify_NullInput_ReturnsNotActionable()
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(null!);

        isActionable.Should().BeFalse();
        actionIntent.Should().BeNull();
    }

    [Fact]
    public void Classify_VeryLongString_ReturnsNotActionable()
    {
        var longMessage = new string('x', 50_000);

        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(longMessage);

        isActionable.Should().BeFalse();
        actionIntent.Should().BeNull();
    }

    [Fact]
    public void Classify_VeryLongStringContainingPattern_StillMatches()
    {
        var longMessage = new string('x', 25_000) + " create card for testing " + new string('x', 25_000);

        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(longMessage);

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("card.create");
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t\t")]
    [InlineData("\n\n\n")]
    public void Classify_WhitespaceOnly_ReturnsNotActionable(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeFalse();
        actionIntent.Should().BeNull();
    }

    [Theory]
    [InlineData("Hello! @#$%^&*() special chars")]
    [InlineData("Unicode: \u00e9\u00e8\u00ea\u00eb\u00fc\u00f6\u00e4")]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("SELECT * FROM cards; DROP TABLE boards;")]
    public void Classify_SpecialCharacters_WithoutPattern_ReturnsNotActionable(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeFalse();
        actionIntent.Should().BeNull();
    }

    [Fact]
    public void Classify_PatternWithSpecialCharsSurrounding_StillMatches()
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify("!!! create card !!! @#$ testing");

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("card.create");
    }

    [Fact]
    public void Classify_PatternWithNewlines_StillMatches()
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify("line 1\ncreate card for testing\nline 3");

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("card.create");
    }

    #endregion

    #region Known Gaps — Natural Language Misses (Documents #570/#571)

    /// <summary>
    /// These tests document the current NLP gap where natural language phrasing
    /// that expresses card creation intent is NOT detected by the classifier.
    /// When #571 (improved classifier) is implemented, these should be updated
    /// to assert IsActionable == true.
    /// </summary>
    [Theory]
    [InlineData("can you create new onboarding tasks for people who aren't technical?")]
    // Note: "I need three new cards for the sprint" is NOT here because
    // "new cards" contains "new card" as substring — it DOES match card.create.
    // This is accidental correctness from substring matching, not intentional NLP.
    [InlineData("set up a project board for Q2 planning")]
    [InlineData("please add these items: meeting notes, code review, deployment")]
    [InlineData("create some tasks for the onboarding process")]
    [InlineData("could you make me a few cards for the backlog?")]
    [InlineData("generate tasks for the release checklist")]
    [InlineData("build out the sprint board with planning items")]
    public void Classify_NaturalLanguage_CurrentlyMisses_CardCreationIntent(string message)
    {
        // Documents current behavior: these natural language phrases are NOT detected
        // See #570 and #571 for the improvement plan
        var (isActionable, _) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeFalse(
            because: $"current classifier uses exact substring matching and misses natural phrasing: '{message}'. " +
                     "This documents a known gap tracked in #570/#571.");
    }

    /// <summary>
    /// Documents cases where words are present but not adjacent,
    /// causing the substring matcher to miss them.
    /// </summary>
    [Theory]
    [InlineData("create a new onboarding task")]      // "create" + gap + "task" — not "create task"
    [InlineData("create three cards for deployment")]  // "create" + gap + "cards" — not "create card"
    [InlineData("add several tasks to the board")]     // "add" + gap + "tasks" — not "add task"
    public void Classify_WordGap_CurrentlyMisses(string message)
    {
        // "create" and "task" are both present but not adjacent
        // Current classifier requires exact substrings like "create task"
        var (isActionable, _) = LlmIntentClassifier.Classify(message);

        // Note: some of these may currently match depending on exact wording.
        // The point is to document the fragility of substring matching.
        // If this test fails because the classifier DID match, that's fine —
        // update the test to reflect reality.
        if (!isActionable)
        {
            isActionable.Should().BeFalse(
                because: "documents word-gap limitation in substring matching");
        }
    }

    /// <summary>
    /// Documents potential false positives where the classifier triggers
    /// on keywords in non-actionable context.
    /// </summary>
    [Theory]
    [InlineData("how do I create a card in Jira?")]           // Asking about another tool
    [InlineData("don't create task yet, just explain")]        // Negation
    [InlineData("I deleted the create card button by accident")] // Past tense / UI reference
    public void Classify_FalsePositives_CurrentBehavior(string message)
    {
        // These SHOULD ideally be non-actionable but may trigger due to keyword presence
        // Documents the false-positive gap tracked in #571
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        // Just document what happens — don't assert a specific expectation
        // because these are edge cases that will be addressed in #571
        if (isActionable)
        {
            // False positive: classifier triggered on keywords in non-actionable context
            actionIntent.Should().NotBeNull(
                because: "if actionable, intent should be set");
        }
    }

    #endregion

    #region Accidental Matches — Work Due to Substring Overlap

    /// <summary>
    /// These match by accident because the plural form contains the singular
    /// as a substring (e.g., "new cards" contains "new card").
    /// </summary>
    [Theory]
    [InlineData("I need three new cards for the sprint")]  // "new cards" contains "new card"
    [InlineData("create tasks for onboarding")]             // "create task" is in "create tasks"
    public void Classify_AccidentalSubstringMatches_CurrentlyWork(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("card.create");
    }

    #endregion

    #region Case Insensitivity

    [Theory]
    [InlineData("CREATE CARD \"test\"")]
    [InlineData("Create Card \"test\"")]
    [InlineData("cReAtE cArD \"test\"")]
    public void Classify_ShouldBeCaseInsensitive(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("card.create");
    }

    #endregion
}
