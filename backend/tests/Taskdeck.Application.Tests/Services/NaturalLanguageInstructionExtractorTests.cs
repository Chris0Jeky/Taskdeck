using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class NaturalLanguageInstructionExtractorTests
{
    #region Card Create — Quoted Titles

    [Theory]
    [InlineData("create card \"My task\"", "create card \"My task\"")]
    [InlineData("add card 'Bug fix for login'", "create card \"Bug fix for login\"")]
    [InlineData("make a card \"Deploy to staging\"", "create card \"Deploy to staging\"")]
    public void Extract_CardCreate_WithQuotedTitle_ReturnsStructuredInstruction(string message, string expected)
    {
        var result = NaturalLanguageInstructionExtractor.Extract(message, "card.create");

        result.Should().ContainSingle();
        result[0].Should().Be(expected);
    }

    #endregion

    #region Card Create — Natural Language Title Extraction

    [Fact]
    public void Extract_CardCreate_NaturalLanguage_ExtractsMeaningfulTitle()
    {
        var result = NaturalLanguageInstructionExtractor.Extract(
            "create new onboarding tasks for people who aren't technical",
            "card.create");

        result.Should().ContainSingle();
        result[0].Should().StartWith("create card \"");
        result[0].Should().ContainEquivalentOf("onboarding");
    }

    [Fact]
    public void Extract_CardCreate_SimplePhrase_ExtractsMeaningfulTitle()
    {
        var result = NaturalLanguageInstructionExtractor.Extract(
            "create tasks for the release checklist",
            "card.create");

        result.Should().ContainSingle();
        result[0].Should().StartWith("create card \"");
    }

    [Fact]
    public void Extract_CardCreate_WithSetUp_ExtractsTitle()
    {
        var result = NaturalLanguageInstructionExtractor.Extract(
            "set up a few cards for sprint planning",
            "card.create");

        result.Should().ContainSingle();
        result[0].Should().StartWith("create card \"");
    }

    [Fact]
    public void Extract_CardCreate_GenerateVerb_ExtractsTitle()
    {
        var result = NaturalLanguageInstructionExtractor.Extract(
            "generate a card for this feature",
            "card.create");

        result.Should().ContainSingle();
        result[0].Should().StartWith("create card \"");
    }

    [Fact]
    public void Extract_CardCreate_BuildVerb_ExtractsTitle()
    {
        var result = NaturalLanguageInstructionExtractor.Extract(
            "build out some tasks for the release",
            "card.create");

        result.Should().ContainSingle();
        result[0].Should().StartWith("create card \"");
    }

    [Fact]
    public void Extract_CardCreate_NewKeyword_ExtractsTitle()
    {
        var result = NaturalLanguageInstructionExtractor.Extract(
            "I need three new cards for the sprint",
            "card.create");

        result.Should().ContainSingle();
        result[0].Should().StartWith("create card \"");
    }

    #endregion

    #region Card Move

    [Fact]
    public void Extract_CardMove_WithIdAndQuotedColumn_ReturnsInstruction()
    {
        var cardId = Guid.NewGuid().ToString();
        var result = NaturalLanguageInstructionExtractor.Extract(
            $"move card {cardId} to column \"Done\"",
            "card.move");

        result.Should().ContainSingle();
        result[0].Should().Be($"move card {cardId} to column \"Done\"");
    }

    [Fact]
    public void Extract_CardMove_WithoutId_ReturnsEmpty()
    {
        var result = NaturalLanguageInstructionExtractor.Extract(
            "move this card to done",
            "card.move");

        result.Should().BeEmpty();
    }

    #endregion

    #region Card Archive

    [Fact]
    public void Extract_CardArchive_WithId_ReturnsInstruction()
    {
        var cardId = Guid.NewGuid().ToString();
        var result = NaturalLanguageInstructionExtractor.Extract(
            $"archive card {cardId}",
            "card.archive");

        result.Should().ContainSingle();
        result[0].Should().Be($"archive card {cardId}");
    }

    [Fact]
    public void Extract_CardArchive_NaturalLanguageWithId_ExtractsId()
    {
        var cardId = Guid.NewGuid().ToString();
        var result = NaturalLanguageInstructionExtractor.Extract(
            $"please remove card {cardId} from the board",
            "card.archive");

        result.Should().ContainSingle();
        result[0].Should().Be($"archive card {cardId}");
    }

    [Fact]
    public void Extract_CardArchive_WithoutId_ReturnsEmpty()
    {
        var result = NaturalLanguageInstructionExtractor.Extract(
            "delete all the old cards",
            "card.archive");

        result.Should().BeEmpty();
    }

    #endregion

    #region Card Update

    [Fact]
    public void Extract_CardUpdate_TitleWithQuotedValue_ReturnsInstruction()
    {
        var cardId = Guid.NewGuid().ToString();
        var result = NaturalLanguageInstructionExtractor.Extract(
            $"update card {cardId} title \"New title\"",
            "card.update");

        result.Should().ContainSingle();
        result[0].Should().Be($"update card {cardId} title \"New title\"");
    }

    [Fact]
    public void Extract_CardUpdate_DescriptionWithQuotedValue_ReturnsInstruction()
    {
        var cardId = Guid.NewGuid().ToString();
        var result = NaturalLanguageInstructionExtractor.Extract(
            $"update card {cardId} description \"New description text\"",
            "card.update");

        result.Should().ContainSingle();
        result[0].Should().Be($"update card {cardId} description \"New description text\"");
    }

    [Fact]
    public void Extract_CardUpdate_WithoutQuotedValue_ReturnsEmpty()
    {
        var cardId = Guid.NewGuid().ToString();
        var result = NaturalLanguageInstructionExtractor.Extract(
            $"update card {cardId} with a better title",
            "card.update");

        result.Should().BeEmpty();
    }

    #endregion

    #region Board Rename

    [Fact]
    public void Extract_BoardRename_WithQuotedName_ReturnsInstruction()
    {
        var result = NaturalLanguageInstructionExtractor.Extract(
            "rename board to \"Sprint 5\"",
            "board.update");

        result.Should().ContainSingle();
        result[0].Should().Be("rename board to \"Sprint 5\"");
    }

    [Fact]
    public void Extract_BoardRename_WithoutQuotedName_ReturnsEmpty()
    {
        var result = NaturalLanguageInstructionExtractor.Extract(
            "rename the board to something better",
            "board.update");

        result.Should().BeEmpty();
    }

    #endregion

    #region Edge Cases

    [Theory]
    [InlineData(null, "card.create")]
    [InlineData("", "card.create")]
    [InlineData("   ", "card.create")]
    public void Extract_NullOrEmptyMessage_ReturnsEmpty(string? message, string intent)
    {
        var result = NaturalLanguageInstructionExtractor.Extract(message!, intent);

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("create a card", null)]
    [InlineData("create a card", "")]
    [InlineData("create a card", "   ")]
    public void Extract_NullOrEmptyIntent_ReturnsEmpty(string message, string? intent)
    {
        var result = NaturalLanguageInstructionExtractor.Extract(message, intent);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Extract_UnknownIntent_ReturnsEmpty()
    {
        var result = NaturalLanguageInstructionExtractor.Extract(
            "do something weird",
            "unknown.intent");

        result.Should().BeEmpty();
    }

    #endregion

    #region CleanExtractedTitle

    [Theory]
    [InlineData("onboarding tasks for non-technical people", "Onboarding tasks for non-technical people")]
    [InlineData("a task for deployment", "Task for deployment")]
    [InlineData("some cards for sprint", "Cards for sprint")]
    [InlineData("the release checklist", "Release checklist")]
    [InlineData("cards", "")]
    [InlineData("task", "")]
    [InlineData("items", "")]
    [InlineData("feature request please", "Feature request")]
    [InlineData("bug fix asap", "Bug fix")]
    public void CleanExtractedTitle_ShouldCleanCorrectly(string input, string expected)
    {
        var result = NaturalLanguageInstructionExtractor.CleanExtractedTitle(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CleanExtractedTitle_NullOrEmpty_ReturnsEmpty(string? input)
    {
        var result = NaturalLanguageInstructionExtractor.CleanExtractedTitle(input!);

        result.Should().BeEmpty();
    }

    #endregion
}
