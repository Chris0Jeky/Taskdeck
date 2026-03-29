using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LlmIntentClassifierTests
{
    #region Card Creation — Exact Patterns (Backward Compatibility)

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

    #region Card Creation — Natural Language Patterns (New)

    [Theory]
    [InlineData("can you create new onboarding tasks for people who aren't technical?")]
    [InlineData("create some tasks for the onboarding process")]
    [InlineData("could you make me a few cards for the backlog?")]
    [InlineData("generate tasks for the release checklist")]
    [InlineData("please add these items: meeting notes, code review, deployment")]
    [InlineData("I need three new cards for the sprint")]
    [InlineData("create tasks for onboarding")]
    [InlineData("create three cards for deployment")]
    [InlineData("add several tasks to the board")]
    [InlineData("create a new onboarding task")]
    [InlineData("generate a card for this feature")]
    [InlineData("build out some tasks for the release")]
    [InlineData("prepare a task for code review")]
    [InlineData("set up a few cards for the sprint")]
    public void Classify_CardCreation_ShouldDetect_NaturalLanguage(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeTrue(
            because: $"'{message}' expresses card creation intent");
        actionIntent.Should().Be("card.create");
    }

    #endregion

    #region Card Operations — Exact Patterns (Backward Compatibility)

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
    /// Fixed: "remove card" now correctly classifies as card.archive
    /// because archive/delete/remove are checked before move.
    /// </summary>
    [Fact]
    public void Classify_RemoveCard_ShouldClassifyAsArchive()
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify("remove card abc123");

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("card.archive",
            because: "archive/delete/remove checks now run before move checks");
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

    #region Board Operations — Exact Patterns (Backward Compatibility)

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

    #region Board Operations — Natural Language (New)

    [Theory]
    [InlineData("set up a project board for Q2 planning")]
    [InlineData("build out the sprint board with planning items")]
    public void Classify_BoardCreation_ShouldDetect_NaturalLanguage(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeTrue(
            because: $"'{message}' expresses board creation intent");
        actionIntent.Should().Be("board.create");
    }

    #endregion

    #region Negative Context — Negations

    [Theory]
    [InlineData("don't create task yet, just explain")]
    [InlineData("do not create a card until I approve")]
    [InlineData("don't add task please")]
    [InlineData("never create tasks automatically")]
    [InlineData("stop creating cards")]
    [InlineData("cancel the add task operation")]
    public void Classify_Negation_ShouldReturnFalse(string message)
    {
        var (isActionable, _) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeFalse(
            because: $"'{message}' contains a negation that should suppress the intent");
    }

    #endregion

    #region Negative Context — Questions About Other Tools

    [Theory]
    [InlineData("how do I create a card in Jira?")]
    [InlineData("how can I add a task in Trello?")]
    [InlineData("where do I create cards in Asana?")]
    public void Classify_OtherToolQuestions_ShouldReturnFalse(string message)
    {
        var (isActionable, _) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeFalse(
            because: $"'{message}' is a question about another tool, not a Taskdeck action");
    }

    /// <summary>
    /// Commands (not questions) mentioning other tools should still classify,
    /// because the user might be saying "create a card like I do in Jira".
    /// </summary>
    [Theory]
    [InlineData("create a card similar to what I have in Jira")]
    [InlineData("add task, I used to do this in Trello")]
    public void Classify_CommandsMentioningOtherTools_ShouldStillMatch(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeTrue(
            because: $"'{message}' is a command, not a question about another tool");
        actionIntent.Should().Be("card.create");
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
    [InlineData("   ")]
    public void Classify_NonActionable_ShouldReturnFalse(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeFalse();
        actionIntent.Should().BeNull();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Classify_NullInput_ShouldReturnFalse()
    {
        // null is not valid for the parameter type, but whitespace-only is
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify("   \t  ");

        isActionable.Should().BeFalse();
        actionIntent.Should().BeNull();
    }

    [Fact]
    public void Classify_VeryLongInput_ShouldNotHang()
    {
        // Ensure regex doesn't catastrophically backtrack on long input
        var longMessage = "please " + string.Join(" ", Enumerable.Repeat("very", 200)) + " create a task for me";

        // Should complete without timeout; may or may not match depending on word count limit
        var (isActionable, _) = LlmIntentClassifier.Classify(longMessage);

        // We just verify it completes without throwing
        _ = isActionable;
    }

    [Fact]
    public void Classify_MixedIntents_FirstMatchWins()
    {
        // "archive" is checked before "move" and "create"
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify("archive card and create task");

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("card.archive",
            because: "archive is checked before create in the classification order");
    }

    #endregion

    #region Case Insensitivity

    [Theory]
    [InlineData("CREATE CARD \"test\"")]
    [InlineData("Create Card \"test\"")]
    [InlineData("cReAtE cArD \"test\"")]
    [InlineData("GENERATE TASKS for release")]
    [InlineData("Build Some Tasks for sprint")]
    public void Classify_ShouldBeCaseInsensitive(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("card.create");
    }

    #endregion

    #region Plural Forms

    [Theory]
    [InlineData("create cards for the team")]
    [InlineData("add tasks to the board")]
    [InlineData("new items for sprint planning")]
    [InlineData("make cards for each milestone")]
    public void Classify_PluralForms_ShouldMatchCardCreate(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("card.create");
    }

    [Theory]
    [InlineData("delete cards from the archive")]
    [InlineData("remove tasks that are done")]
    public void Classify_PluralForms_ShouldMatchCardArchive(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("card.archive");
    }

    #endregion

    #region Broader Verb Coverage

    [Theory]
    [InlineData("generate a task for deployment")]
    [InlineData("build a card for the feature")]
    [InlineData("prepare tasks for the meeting")]
    [InlineData("set up items for review")]
    public void Classify_BroaderVerbs_ShouldMatchCardCreate(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeTrue();
        actionIntent.Should().Be("card.create");
    }

    #endregion

    #region Word-Distance Matching

    [Theory]
    [InlineData("create a new onboarding task")]
    [InlineData("create three cards for deployment")]
    [InlineData("add several tasks to the board")]
    [InlineData("make me a couple of cards")]
    [InlineData("generate some important tasks")]
    public void Classify_WordGap_ShouldNowMatch(string message)
    {
        var (isActionable, actionIntent) = LlmIntentClassifier.Classify(message);

        isActionable.Should().BeTrue(
            because: $"'{message}' has verb and noun with words in between");
        actionIntent.Should().Be("card.create");
    }

    #endregion
}
