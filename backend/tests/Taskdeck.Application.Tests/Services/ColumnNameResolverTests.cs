using FluentAssertions;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ColumnNameResolverTests
{
    [Fact]
    public void Resolve_ShouldFind_ExactNameMatch()
    {
        var boardId = Guid.NewGuid();
        var backlog = TestDataBuilder.CreateColumn(boardId, "Backlog", 0);
        var done = TestDataBuilder.CreateColumn(boardId, "Done", 1);

        var resolution = ColumnNameResolver.Resolve([backlog, done], "Backlog");

        resolution.Outcome.Should().Be(ColumnResolutionOutcome.Found);
        resolution.Column.Should().BeSameAs(backlog);
    }

    [Fact]
    public void Resolve_ShouldFind_CaseInsensitiveMatch()
    {
        var boardId = Guid.NewGuid();
        var backlog = TestDataBuilder.CreateColumn(boardId, "Backlog", 0);

        var resolution = ColumnNameResolver.Resolve([backlog], "bAcKlOg");

        resolution.Outcome.Should().Be(ColumnResolutionOutcome.Found);
        resolution.Column.Should().BeSameAs(backlog);
    }

    [Fact]
    public void Resolve_ShouldReturnNotFound_WhenNoColumnMatches()
    {
        var boardId = Guid.NewGuid();
        var backlog = TestDataBuilder.CreateColumn(boardId, "Backlog", 0);

        var resolution = ColumnNameResolver.Resolve([backlog], "Missing");

        resolution.Outcome.Should().Be(ColumnResolutionOutcome.NotFound);
        resolution.Column.Should().BeNull();
    }

    [Fact]
    public void Resolve_ShouldReturnNotFound_WhenBoardHasNoColumns()
    {
        var resolution = ColumnNameResolver.Resolve([], "Backlog");

        resolution.Outcome.Should().Be(ColumnResolutionOutcome.NotFound);
        resolution.Column.Should().BeNull();
    }

    [Fact]
    public void Resolve_ShouldReturnAmbiguous_WhenTwoColumnsShareTheName()
    {
        var boardId = Guid.NewGuid();
        var first = TestDataBuilder.CreateColumn(boardId, "Backlog", 0);
        var second = TestDataBuilder.CreateColumn(boardId, "Backlog", 1);

        var resolution = ColumnNameResolver.Resolve([first, second], "Backlog");

        resolution.Outcome.Should().Be(ColumnResolutionOutcome.Ambiguous);
        resolution.Column.Should().BeNull();
    }

    [Fact]
    public void Resolve_ShouldReturnAmbiguous_WhenDuplicatesDifferOnlyByCase()
    {
        var boardId = Guid.NewGuid();
        var first = TestDataBuilder.CreateColumn(boardId, "Backlog", 0);
        var second = TestDataBuilder.CreateColumn(boardId, "BACKLOG", 1);

        var resolution = ColumnNameResolver.Resolve([first, second], "backlog");

        resolution.Outcome.Should().Be(ColumnResolutionOutcome.Ambiguous);
    }

    [Fact]
    public void Resolve_ShouldReturnAmbiguous_WhenThreeColumnsShareTheName()
    {
        var boardId = Guid.NewGuid();
        var columns = new[]
        {
            TestDataBuilder.CreateColumn(boardId, "Backlog", 0),
            TestDataBuilder.CreateColumn(boardId, "Backlog", 1),
            TestDataBuilder.CreateColumn(boardId, "Backlog", 2)
        };

        var resolution = ColumnNameResolver.Resolve(columns, "Backlog");

        resolution.Outcome.Should().Be(ColumnResolutionOutcome.Ambiguous);
    }

    [Fact]
    public void AmbiguousMessage_ShouldNameTheColumnAndTheRemedy()
    {
        var message = ColumnNameResolver.AmbiguousMessage("Backlog");

        message.Should().Contain("Backlog");
        message.Should().Contain("ambiguous");
        message.Should().Contain("rename");
    }
}
