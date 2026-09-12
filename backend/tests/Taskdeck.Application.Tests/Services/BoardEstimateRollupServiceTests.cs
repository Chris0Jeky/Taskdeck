using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class BoardEstimateRollupServiceTests
{
    private readonly Guid _boardId = Guid.NewGuid();
    private readonly User _owner = new("owner", "owner@example.com", "hash");
    private readonly User _member = new("member", "member@example.com", "hash");
    private readonly Mock<IUnitOfWork> _unit = new();
    private readonly Mock<ICardRepository> _cards = new();
    private readonly Mock<IColumnRepository> _columns = new();
    private readonly Mock<ICardAssignmentStore> _assignments = new();
    private readonly Mock<IAuthorizationService> _authorization = new();

    private BoardEstimateRollupService CreateService(IReadOnlyList<Card> cards, params Column[] columns)
    {
        _unit.SetupGet(unit => unit.Cards).Returns(_cards.Object);
        _unit.SetupGet(unit => unit.Columns).Returns(_columns.Object);
        _cards.Setup(repo => repo.GetForEstimateRollupsAsync(_boardId, It.IsAny<CancellationToken>())).ReturnsAsync(cards);
        _columns.Setup(repo => repo.GetByBoardIdAsync(_boardId, It.IsAny<CancellationToken>())).ReturnsAsync(columns);
        _assignments.Setup(store => store.ReadParticipantsAsync(_boardId, It.IsAny<CancellationToken>())).ReturnsAsync([_owner, _member]);
        _authorization.Setup(auth => auth.CanReadBoardAsync(_owner.Id, _boardId)).ReturnsAsync(Result.Success(true));
        return new(_unit.Object, _assignments.Object, _authorization.Object);
    }

    private Card Card(Column column, int? estimate)
    {
        var card = new Card(_boardId, column.Id, "Estimate", position: 0);
        card.SetEstimatedEffortMinutes(estimate);
        return card;
    }

    [Fact]
    public async Task MixedEstimatesAndOverlappingAssignmentsKeepBoardAndParentTotalsIndependent()
    {
        var next = new Column(_boardId, "Next", 0);
        var done = new Column(_boardId, "Done", 1);
        var empty = new Column(_boardId, "Empty", 2);
        var unknown = Card(next, null);
        var zero = Card(next, 0);
        var parent = Card(next, 90);
        var child = Card(done, 30);
        child.SetParent(parent.Id);
        parent.ReplaceAssignments([_owner.Id, _member.Id], _owner.Id);
        unknown.ReplaceAssignments([_member.Id], _owner.Id);
        var archived = Card(done, 999); archived.Archive();
        var foreign = new Card(Guid.NewGuid(), done.Id, "Foreign", position: 0); foreign.SetEstimatedEffortMinutes(999);
        var service = CreateService([unknown, zero, parent, child, archived, foreign], next, done, empty);

        var result = await service.GetAsync(_boardId, _owner.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Board.Should().Be(new EstimateTotalsDto(4, 120, 1));
        result.Value.Columns.Select(column => column.Totals).Should().Equal(
            new EstimateTotalsDto(3, 90, 1), new EstimateTotalsDto(1, 30, 0), new EstimateTotalsDto(0, 0, 0));
        result.Value.Participants.Single(p => p.UserId == _owner.Id).Totals.Should().Be(new EstimateTotalsDto(1, 90, 0));
        result.Value.Participants.Single(p => p.UserId == _member.Id).Totals.Should().Be(new EstimateTotalsDto(2, 90, 1));
        result.Value.Unassigned.Should().Be(new EstimateTotalsDto(2, 30, 0));
        parent.EstimatedEffortMinutes.Should().Be(90);
        _cards.Verify(repo => repo.GetForEstimateRollupsAsync(_boardId, It.IsAny<CancellationToken>()), Times.Once);
        _assignments.Verify(store => store.ReadParticipantsAsync(_boardId, It.IsAny<CancellationToken>()), Times.Once);
        _unit.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CurrentReadsReflectMoveArchiveRestoreAndRevokedParticipant()
    {
        var next = new Column(_boardId, "Next", 0);
        var done = new Column(_boardId, "Done", 1);
        var card = Card(next, 60);
        card.ReplaceAssignments([_member.Id], _owner.Id);
        var service = CreateService([card], next, done);
        (await service.GetAsync(_boardId, _owner.Id)).Value.Unassigned.CardCount.Should().Be(0);
        card.MoveToColumn(done.Id, 0);
        // Defensive filtering makes a stale assignment ineligible immediately on revocation.
        _assignments.Setup(store => store.ReadParticipantsAsync(_boardId, It.IsAny<CancellationToken>())).ReturnsAsync([_owner]);
        var moved = (await service.GetAsync(_boardId, _owner.Id)).Value;
        moved.Columns[0].Totals.CardCount.Should().Be(0);
        moved.Columns[1].Totals.KnownEstimateMinutes.Should().Be(60);
        moved.Participants.Should().ContainSingle(p => p.UserId == _owner.Id && p.Totals.CardCount == 0);
        moved.Unassigned.Should().Be(new EstimateTotalsDto(1, 60, 0));
        card.Archive();
        (await service.GetAsync(_boardId, _owner.Id)).Value.Board.CardCount.Should().Be(0);
        card.Restore();
        (await service.GetAsync(_boardId, _owner.Id)).Value.Board.KnownEstimateMinutes.Should().Be(60);
    }

    [Fact]
    public async Task SumsUse64BitMinutes()
    {
        var column = new Column(_boardId, "Many estimates", 0);
        var service = CreateService(Enumerable.Range(0, 2200).Select(_ => Card(column, 1_000_000)).ToArray(), column);
        var result = await service.GetAsync(_boardId, _owner.Id);
        result.Value.Board.KnownEstimateMinutes.Should().Be(2_200_000_000L);
        result.Value.Columns[0].Totals.KnownEstimateMinutes.Should().Be(2_200_000_000L);
        result.Value.Unassigned.KnownEstimateMinutes.Should().Be(2_200_000_000L);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DenialOrAuthorizationFailureNeverReadsRollupData(bool failure)
    {
        var service = CreateService([]);
        _authorization.Setup(auth => auth.CanReadBoardAsync(_owner.Id, _boardId)).ReturnsAsync(failure
            ? Result.Failure<bool>(ErrorCodes.NotFound, "Board not found") : Result.Success(false));
        var result = await service.GetAsync(_boardId, _owner.Id);
        result.ErrorCode.Should().Be(failure ? ErrorCodes.NotFound : ErrorCodes.Forbidden);
        _cards.Verify(repo => repo.GetForEstimateRollupsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _assignments.Verify(store => store.ReadParticipantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
