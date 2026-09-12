using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ExternalImportArchivedMatchTests
{
    [Theory]
    [InlineData(true, "title")]
    [InlineData(false, "title")]
    [InlineData(true, "description")]
    [InlineData(false, "description")]
    [InlineData(true, "move")]
    [InlineData(false, "move")]
    public async Task ChangedArchivedMatch_ShouldConflictBeforeAnyWrite(bool dryRun, string change)
    {
        var fixture = new Fixture();
        var card = fixture.AddCard("archived", archived: true, inTarget: change != "move");
        var before = StateOf(card);
        var candidate = new ExternalImportCandidate(7, "archived",
            change == "title" ? "Changed title" : card.Title,
            change == "description" ? card.Description + "\nChanged details" : card.Description);

        var result = await fixture.ImportAsync([candidate], dryRun);

        result.IsSuccess.Should().BeTrue();
        result.Value.Applied.Should().BeFalse();
        result.Value.DryRun.Should().Be(dryRun);
        result.Value.RowsUpdated.Should().Be(0);
        result.Value.RowsCreated.Should().Be(0);
        result.Value.RowsSkipped.Should().Be(0);
        var conflict = result.Value.Conflicts.Should().ContainSingle().Subject;
        conflict.Code.Should().Be("ArchivedExistingMatch");
        conflict.Path.Should().Be("$.rows[7]");
        conflict.ExistingValue.Should().Contain(card.Id.ToString());
        conflict.IncomingValue.Should().Be("archived");
        conflict.Message.Should().Contain("Restore").And.Contain("explicitly");
        StateOf(card).Should().Be(before);
        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MixedBatch_ShouldDescribeAllRowsButRefuseTheWholeBatch(bool dryRun)
    {
        var fixture = new Fixture();
        var changedArchived = fixture.AddCard("changed-archive", archived: true);
        var unchangedArchived = fixture.AddCard("unchanged-archive", archived: true);
        var active = fixture.AddCard("active");
        var before = fixture.Board.Cards.Select(StateOf).ToArray();
        // Put the conflicting row last: earlier valid plans must not become early writes.
        List<ExternalImportCandidate> candidates =
        [
            new(2, "active", "Updated active", active.Description),
            new(3, "new", "New contact", DescriptionFor("new")),
            new(4, "unchanged-archive", unchangedArchived.Title, unchangedArchived.Description),
            new(5, "changed-archive", "Changed archive", changedArchived.Description)
        ];

        var result = await fixture.ImportAsync(candidates, dryRun);

        result.IsSuccess.Should().BeTrue();
        result.Value.Applied.Should().BeFalse();
        result.Value.RowsReceived.Should().Be(4);
        result.Value.RowsParsed.Should().Be(4);
        result.Value.RowsCreated.Should().Be(1);
        result.Value.RowsUpdated.Should().Be(1);
        result.Value.RowsSkipped.Should().Be(1);
        result.Value.Conflicts.Should().ContainSingle()
            .Which.Code.Should().Be("ArchivedExistingMatch");
        fixture.Board.Cards.Select(StateOf).Should().Equal(before);
        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnchangedArchive_ShouldSkipWithoutBlockingActiveCreatesUpdatesAndMoves(bool dryRun)
    {
        var fixture = new Fixture();
        var archived = fixture.AddCard("unchanged", archived: true);
        var active = fixture.AddCard("active", inTarget: false);
        var archivedBefore = StateOf(archived);
        var activeBefore = StateOf(active);
        List<ExternalImportCandidate> candidates =
        [
            new(2, "unchanged", archived.Title, archived.Description),
            new(3, "active", "Updated active", active.Description),
            new(4, "new", "New contact", DescriptionFor("new"))
        ];

        var result = await fixture.ImportAsync(candidates, dryRun);

        result.IsSuccess.Should().BeTrue();
        result.Value.Applied.Should().Be(!dryRun);
        result.Value.Conflicts.Should().BeEmpty();
        result.Value.RowsSkipped.Should().Be(1);
        result.Value.RowsCreated.Should().Be(1);
        result.Value.RowsUpdated.Should().Be(1);
        StateOf(archived).Should().Be(archivedBefore);
        if (dryRun)
        {
            StateOf(active).Should().Be(activeBefore);
            fixture.AssertNoWrites();
        }
        else
        {
            active.Title.Should().Be("Updated active");
            active.ColumnId.Should().Be(fixture.Target.Id);
            active.IsArchived.Should().BeFalse();
            fixture.Cards.Verify(repository => repository.AddAsync(
                It.Is<Card>(card => card.Title == "New contact" &&
                    card.ColumnId == fixture.Target.Id && !card.IsArchived),
                It.IsAny<CancellationToken>()), Times.Once);
            fixture.UnitOfWork.Verify(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
            fixture.UnitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
            fixture.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
            fixture.UnitOfWork.Verify(unit => unit.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
            fixture.Board.CardMutationMarker.Should().Be(1);
        }
    }

    [Fact]
    public async Task PreviewAndApply_ShouldReturnTheSameArchivedConflictWithTheRealCsvAdapter()
    {
        var fixture = new Fixture();
        var adapter = new CsvExternalImportAdapter();
        var request = new ExternalImportRequestDto(ExternalImportProviders.Csv,
            "Display Name,Email\nOriginal Name,person@example.invalid", fixture.Target.Name, DryRun: true);
        var original = adapter.Parse(request);
        original.IsSuccess.Should().BeTrue();
        var candidate = original.Value.Candidates.Should().ContainSingle().Subject;
        var card = new Card(fixture.Board.Id, fixture.Target.Id, candidate.Title, candidate.Description);
        card.Archive();
        fixture.Attach(card, fixture.Target);
        var before = StateOf(card);
        var service = new ExternalImportService(fixture.UnitOfWork.Object, [adapter]);
        var changed = request with { Payload = "Display Name,Email\nChanged Name,person@example.invalid" };

        var preview = await service.ImportToBoardAsync(fixture.Board.Id, changed);
        var applied = await service.ImportToBoardAsync(fixture.Board.Id, changed with { DryRun = false });

        preview.IsSuccess.Should().BeTrue();
        applied.IsSuccess.Should().BeTrue();
        preview.Value.Conflicts.Should().ContainSingle().Which.Code.Should().Be("ArchivedExistingMatch");
        applied.Value.Should().BeEquivalentTo(preview.Value with { DryRun = false });
        applied.Value.Applied.Should().BeFalse();
        StateOf(card).Should().Be(before);
        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DuplicateArchivedAndActiveMatches_ShouldRemainAmbiguous(bool dryRun)
    {
        var fixture = new Fixture();
        var archived = fixture.AddCard("duplicate", archived: true);
        fixture.AddCard("duplicate");
        var before = fixture.Board.Cards.Select(StateOf).ToArray();

        var result = await fixture.ImportAsync(
            [new(2, "duplicate", "Changed", archived.Description)], dryRun);

        result.IsSuccess.Should().BeTrue();
        result.Value.Applied.Should().BeFalse();
        result.Value.RowsUpdated.Should().Be(0);
        result.Value.RowsCreated.Should().Be(0);
        result.Value.Conflicts.Select(conflict => conflict.Code).Should().Equal(
            "ExistingDuplicateDedupeKey", "AmbiguousExistingMatch");
        fixture.Board.Cards.Select(StateOf).Should().Equal(before);
        fixture.AssertNoWrites();
    }

    private static (Guid Id, string Title, string Description, Guid ColumnId, int Position,
        bool IsArchived, DateTimeOffset UpdatedAt) StateOf(Card card) =>
        (card.Id, card.Title, card.Description, card.ColumnId, card.Position, card.IsArchived, card.UpdatedAt);

    private static string DescriptionFor(string key) => ExternalImportMetadata.CardDescriptionPrefix +
        JsonSerializer.Serialize(new
        {
            provider = ExternalImportProviders.Csv,
            profile = ExternalImportProfiles.OutreachContactsV1,
            dedupeKey = key
        }) + "\nImported details";

    private sealed class Fixture
    {
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<ICardRepository> Cards { get; } = new();
        public Board Board { get; } = new("Import Board", ownerId: Guid.NewGuid());
        public Column Target { get; }
        private Column Source { get; }

        public Fixture()
        {
            Target = new Column(Board.Id, "Imported", 0);
            Source = new Column(Board.Id, "Original", 1);
            AddToCollection(Board, "_columns", Target);
            AddToCollection(Board, "_columns", Source);
            var boards = new Mock<IBoardRepository>();
            boards.Setup(repository => repository.GetByIdWithDetailsAsync(Board.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Board);
            UnitOfWork.Setup(unit => unit.Boards).Returns(boards.Object);
            UnitOfWork.Setup(unit => unit.Cards).Returns(Cards.Object);
            UnitOfWork.Setup(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            UnitOfWork.Setup(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            UnitOfWork.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Cards.Setup(repository => repository.AddAsync(It.IsAny<Card>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Card card, CancellationToken _) => card);
        }

        public Card AddCard(string key, bool archived = false, bool inTarget = true)
        {
            var column = inTarget ? Target : Source;
            var card = new Card(Board.Id, column.Id, $"Contact {key}", DescriptionFor(key), position: column.Cards.Count);
            if (archived) card.Archive();
            Attach(card, column);
            return card;
        }

        public void Attach(Card card, Column column)
        {
            AddToCollection(Board, "_cards", card);
            AddToCollection(column, "_cards", card);
        }

        public Task<Result<ExternalImportResultDto>> ImportAsync(List<ExternalImportCandidate> candidates, bool dryRun)
        {
            var parsed = new ExternalImportParseResult(ExternalImportProviders.Csv,
                ExternalImportProfiles.OutreachContactsV1, candidates.Count, candidates.Count, candidates, []);
            var service = new ExternalImportService(UnitOfWork.Object, [new FakeAdapter(parsed)]);
            return service.ImportToBoardAsync(Board.Id,
                new ExternalImportRequestDto(ExternalImportProviders.Csv, "unused", Target.Name, dryRun));
        }

        public void AssertNoWrites()
        {
            UnitOfWork.Verify(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
            UnitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
            UnitOfWork.Verify(unit => unit.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
            Cards.Verify(repository => repository.AddAsync(It.IsAny<Card>(), It.IsAny<CancellationToken>()), Times.Never);
            Cards.Verify(repository => repository.UpdateAsync(It.IsAny<Card>(), It.IsAny<CancellationToken>()), Times.Never);
            Cards.Verify(repository => repository.DeleteAsync(It.IsAny<Card>(), It.IsAny<CancellationToken>()), Times.Never);
            Board.CardMutationMarker.Should().Be(0);
        }

        private static void AddToCollection<T>(object target, string name, T value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            var collection = field?.GetValue(target) as IList<T>;
            collection.Should().NotBeNull($"fixture navigation {name} must exist");
            collection!.Add(value);
        }
    }

    private sealed class FakeAdapter(ExternalImportParseResult result) : IExternalImportAdapter
    {
        public string Provider => ExternalImportProviders.Csv;
        public Result<ExternalImportParseResult> Parse(ExternalImportRequestDto request) => Result.Success(result);
    }
}
