using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class CardEstimateServiceTests
{
    private readonly Board _board = new("Effort");
    private readonly Card _card;
    private readonly Mock<IUnitOfWork> _work = new();
    private readonly Mock<ICardRepository> _cards = new();
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly Mock<IBoardRealtimeNotifier> _notifier = new();
    private readonly CardService _service;

    public CardEstimateServiceTests()
    {
        _card = new Card(_board.Id, Guid.NewGuid(), "Retain", "Description");
        var boards = new Mock<IBoardRepository>();
        boards.Setup(r => r.GetByIdAsync(_board.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_board);
        _cards.Setup(r => r.GetByIdWithLabelsAsync(_card.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_card);
        _cards.Setup(r => r.TryGuardVersionAsync(_card.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _work.SetupGet(w => w.Boards).Returns(boards.Object);
        _work.SetupGet(w => w.Cards).Returns(_cards.Object);
        _work.SetupGet(w => w.AuditLogs).Returns(_audit.Object);
        _work.Setup(w => w.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _service = new CardService(_work.Object, _notifier.Object);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(1_000_001, false)]
    [InlineData(0, true)]
    public async Task InvalidEstimate_DoesNotMutateOtherFieldsOrSave(int minutes, bool clear)
    {
        var version = _card.UpdatedAt;
        var result = await _service.UpdateCardAsync(_card.Id,
            new("Must not change", null, null, null, null, null, version,
                EstimatedEffortMinutes: minutes, ClearEstimatedEffort: clear));

        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _card.Title.Should().Be("Retain");
        _card.EstimatedEffortMinutes.Should().BeNull();
        _card.UpdatedAt.Should().Be(version);
        _work.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IntentionalEstimateWrite_RequiresVersion(bool clear)
    {
        var result = await _service.UpdateCardAsync(_card.Id,
            new(null, null, null, null, null, null, EstimatedEffortMinutes: clear ? null : 0, ClearEstimatedEffort: clear));
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _work.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Estimate_SetZeroAndClear_StageActorAuditBeforeSingleSaveAndNotify()
    {
        var actor = Guid.NewGuid();
        var steps = new List<string>();
        _audit.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) =>
            {
                log.UserId.Should().Be(actor);
                log.Action.Should().Be(AuditAction.Updated);
                log.EntityId.Should().Be(_card.Id);
                steps.Add(log.Changes!);
            }).Returns<AuditLog, CancellationToken>((log, _) => Task.FromResult(log));
        _work.Setup(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => steps.Add("save")).ReturnsAsync(1);
        _notifier.Setup(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()))
            .Callback<BoardRealtimeEvent, CancellationToken>((evt, _) =>
            {
                evt.BoardId.Should().Be(_board.Id);
                evt.Operation.Should().Be("updated");
                steps.Add("notify");
            }).Returns(Task.CompletedTask);

        var set = await _service.UpdateCardAsync(_card.Id, Patch(0), actor);
        set.Value.EstimatedEffortMinutes.Should().Be(0);
        var clear = await _service.UpdateCardAsync(_card.Id, Patch(null, true), actor);
        clear.Value.EstimatedEffortMinutes.Should().BeNull();

        steps.Should().Equal("Estimated effort: unknown -> 0m", "save", "notify",
            "Estimated effort: 0m -> unknown", "save", "notify");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, false)]
    [InlineData(90, false)]
    public async Task RepeatedEstimate_IsNoOp(int? minutes, bool clear)
    {
        _card.SetEstimatedEffortMinutes(minutes);
        var version = _card.UpdatedAt;
        var result = await _service.UpdateCardAsync(_card.Id, Patch(minutes, clear));
        result.IsSuccess.Should().BeTrue();
        result.Value.UpdatedAt.Should().Be(version);
        _cards.Verify(r => r.TryGuardVersionAsync(_card.Id, version, It.IsAny<CancellationToken>()), Times.Once);
        _work.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _audit.Verify(a => a.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RepeatedEstimate_ReturnsConflictWhenAtomicVersionGuardLosesRace()
    {
        _card.SetEstimatedEffortMinutes(90);
        var version = _card.UpdatedAt;
        _cards.Setup(r => r.TryGuardVersionAsync(_card.Id, version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.UpdateCardAsync(_card.Id, Patch(90));

        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        _card.EstimatedEffortMinutes.Should().Be(90);
        _card.UpdatedAt.Should().Be(version);
        _work.Verify(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _audit.Verify(a => a.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NullAndOmission_KeepEstimateDuringUnversionedOrdinaryUpdate()
    {
        _card.SetEstimatedEffortMinutes(90);
        foreach (var dto in new[] {
            new UpdateCardDto("Title one", null, null, null, null, null),
            new UpdateCardDto("Title two", null, null, null, null, null, EstimatedEffortMinutes: null) })
        {
            var result = await _service.UpdateCardAsync(_card.Id, dto);
            result.IsSuccess.Should().BeTrue();
            result.Value.EstimatedEffortMinutes.Should().Be(90);
        }
    }

    [Fact]
    public async Task FailedSave_DoesNotNotifySuccessfulEstimateUpdate()
    {
        _work.Setup(w => w.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException(ErrorCodes.Conflict, "Concurrent write"));
        var result = await _service.UpdateCardAsync(_card.Id, Patch(90));
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        _notifier.Verify(n => n.NotifyBoardMutationAsync(It.IsAny<BoardRealtimeEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private UpdateCardDto Patch(int? minutes, bool clear = false) =>
        new(null, null, null, null, null, null, _card.UpdatedAt,
            EstimatedEffortMinutes: minutes, ClearEstimatedEffort: clear);
}
