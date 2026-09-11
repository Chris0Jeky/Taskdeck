using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// #2934: a card archive/restore applied through a proposal must not tell realtime subscribers
/// anything until the executor's outer transaction actually commits, while a standalone API
/// archive/restore keeps notifying immediately. The recording notifier below captures the
/// transaction state at publish time, so these tests pin the ordering itself and not merely the
/// fact that an event was eventually emitted.
/// </summary>
public class ProposalLifecycleNotificationTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IAutomationProposalService> _proposalServiceMock = new();
    private readonly Mock<IAutomationPolicyEngine> _policyEngineMock = new();
    private readonly Mock<IAutomationProposalRepository> _proposalRepoMock = new();
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock = new();
    private readonly Mock<IBoardRepository> _boardRepoMock = new();
    private readonly Mock<IColumnRepository> _columnRepoMock = new();
    private readonly Mock<ICardRepository> _cardRepoMock = new();
    private readonly Mock<IProposalRevisionRepository> _proposalRevisionRepoMock = new();

    private readonly RecordingBoardRealtimeNotifier _notifier;
    private readonly CardService _cardService;
    private readonly AutomationExecutorService _executor;

    private bool _committed;
    private bool _rolledBack;

    public ProposalLifecycleNotificationTests()
    {
        _unitOfWorkMock.Setup(u => u.AutomationProposals).Returns(_proposalRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.AuditLogs).Returns(_auditLogRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.ProposalRevisions).Returns(_proposalRevisionRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _unitOfWorkMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _committed = true)
            .Returns(Task.CompletedTask);
        _unitOfWorkMock.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _rolledBack = true)
            .Returns(Task.CompletedTask);

        _auditLogRepoMock.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuditLog log, CancellationToken _) => log);
        _cardRepoMock.Setup(r => r.StageDependencyProjectionInvalidationAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _cardRepoMock.Setup(r => r.GetHierarchyByBoardIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Card>());
        _policyEngineMock.Setup(e => e.GuardProposalDecisionWritesAsync(
                It.IsAny<IEnumerable<Guid?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _notifier = new RecordingBoardRealtimeNotifier(() => _committed);
        _cardService = new CardService(_unitOfWorkMock.Object, _notifier);
        _executor = new AutomationExecutorService(
            _unitOfWorkMock.Object,
            _proposalServiceMock.Object,
            _policyEngineMock.Object,
            _cardService,
            new BoardService(_unitOfWorkMock.Object),
            new ColumnService(_unitOfWorkMock.Object),
            logger: null,
            assignments: null,
            realtimeNotifier: _notifier);
    }

    [Fact]
    public async Task ExecuteProposal_ShouldPublishNoLifecycleNotification_WhenALaterOperationRollsTheArchiveBack()
    {
        var (board, column, card) = SeedBoard(archived: false);
        var proposalId = Guid.NewGuid();
        var operations = new List<ProposalOperationDto>
        {
            LifecycleOperation(proposalId, sequence: 0, card, archive: true),
            // A target type the registry refuses outright: it fails after the archive was staged
            // and saved, which is exactly the window the premature notification lived in.
            new(Guid.NewGuid(), proposalId, 1, "update", "widget", null, "{}", "key-fail", null)
        };
        ArrangeApprovedProposal(proposalId, board.Id, operations);

        var result = await _executor.ExecuteProposalAsync(proposalId, "execution-key");

        result.IsSuccess.Should().BeFalse();
        // Guard against a vacuous pass: an empty publish list only proves anything if the archive
        // operation itself really ran and was saved inside the transaction before the failure.
        card.IsArchived.Should().BeTrue("the archive operation must have applied before the failure");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _rolledBack.Should().BeTrue("the failing operation must roll the archive back");
        _committed.Should().BeFalse();
        _notifier.Published.Should().BeEmpty(
            "a lifecycle change that was rolled back must never reach realtime subscribers");
    }

    [Fact]
    public async Task ExecuteProposal_ShouldDeferTheDetachedChildEventsTooAndPublishThemAfterCommit()
    {
        // The archive detaches children in the same staged write, so those card.updated events are
        // exactly as premature as the lifecycle event itself and must ride the same bridge.
        var (board, column, card) = SeedBoard(archived: false);
        var child = TestDataBuilder.CreateCard(board.Id, column.Id, "Child");
        child.SetParent(card.Id);
        _cardRepoMock.Setup(r => r.GetHierarchyByBoardIdAsync(board.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { card, child });

        var proposalId = Guid.NewGuid();
        var operations = new List<ProposalOperationDto>
        {
            new(
                Guid.NewGuid(),
                proposalId,
                0,
                "archive-lifecycle",
                "card",
                null,
                $$"""
                  {"cardId":"{{card.Id}}","expectedUpdatedAt":"{{card.UpdatedAt:O}}","expectedChildrenFingerprint":"{{CardService.ChildrenFingerprint(new[] { child })}}"}
                  """,
                "key-lifecycle-child",
                null)
        };
        ArrangeApprovedProposal(proposalId, board.Id, operations);

        var result = await _executor.ExecuteProposalAsync(proposalId, "execution-key");

        result.IsSuccess.Should().BeTrue();
        child.ParentCardId.Should().BeNull("the archive detaches the child in the same write");
        _notifier.Published.Should().HaveCount(2);
        _notifier.Published.Should().OnlyContain(p => p.CommittedAtPublishTime,
            "every event staged inside the transaction waits for the commit");
        _notifier.Published.Select(p => (p.Mutation.Operation, p.Mutation.EntityId))
            .Should().Equal(("archived", (Guid?)card.Id), ("updated", child.Id));
    }

    [Fact]
    public async Task ExecuteProposal_ShouldStillNotifyThroughTheCardService_WhenNoRealtimeNotifierWasSupplied()
    {
        // An executor built without a notifier has nothing to flush into. It must fall back to the
        // card service's own notifier rather than stage the event into a buffer that drops it:
        // a silently swallowed lifecycle event would be a worse regression than the one being fixed.
        var executorWithoutNotifier = new AutomationExecutorService(
            _unitOfWorkMock.Object,
            _proposalServiceMock.Object,
            _policyEngineMock.Object,
            _cardService,
            new BoardService(_unitOfWorkMock.Object),
            new ColumnService(_unitOfWorkMock.Object));

        var (board, column, card) = SeedBoard(archived: false);
        var proposalId = Guid.NewGuid();
        var operations = new List<ProposalOperationDto>
        {
            LifecycleOperation(proposalId, sequence: 0, card, archive: true)
        };
        ArrangeApprovedProposal(proposalId, board.Id, operations);

        var result = await executorWithoutNotifier.ExecuteProposalAsync(proposalId, "execution-key");

        result.IsSuccess.Should().BeTrue();
        _notifier.Published.Should().ContainSingle();
        _notifier.Published.Single().Mutation.Operation.Should().Be("archived");
    }

    [Theory]
    [InlineData(true, "archived")]
    [InlineData(false, "restored")]
    public async Task ExecuteProposal_ShouldPublishOneLifecycleNotificationAfterCommit_WhenTheProposalSucceeds(
        bool archive, string expectedOperation)
    {
        var (board, column, card) = SeedBoard(archived: !archive);
        var proposalId = Guid.NewGuid();
        var operations = new List<ProposalOperationDto>
        {
            LifecycleOperation(proposalId, sequence: 0, card, archive)
        };
        ArrangeApprovedProposal(proposalId, board.Id, operations);

        var result = await _executor.ExecuteProposalAsync(proposalId, "execution-key");

        result.IsSuccess.Should().BeTrue();
        _committed.Should().BeTrue();
        _rolledBack.Should().BeFalse();

        _notifier.Published.Should().ContainSingle("one lifecycle change publishes exactly one event");
        var published = _notifier.Published.Single();
        published.Mutation.BoardId.Should().Be(board.Id);
        published.Mutation.EntityType.Should().Be("card");
        published.Mutation.Operation.Should().Be(expectedOperation);
        published.Mutation.EntityId.Should().Be(card.Id);
        published.CommittedAtPublishTime.Should().BeTrue(
            "the event must be published after the proposal transaction commits, not before");
    }

    [Theory]
    [InlineData(true, "archived")]
    [InlineData(false, "restored")]
    public async Task SetArchivedAsync_ShouldPublishImmediately_WhenCalledOutsideAProposalTransaction(
        bool archive, string expectedOperation)
    {
        // The standalone API lane passes no notification sink, so it must behave exactly as before:
        // notify as soon as the write is saved, with no transaction involved at all.
        var (board, column, card) = SeedBoard(archived: !archive);

        var result = await _cardService.SetArchivedAsync(
            board.Id, card.Id, archive, new CardLifecycleDto(card.UpdatedAt, null), Guid.NewGuid());

        result.IsSuccess.Should().BeTrue();
        _committed.Should().BeFalse("a direct archive/restore opens no transaction");
        _notifier.Published.Should().ContainSingle();
        _notifier.Published.Single().Mutation.Operation.Should().Be(expectedOperation);
        _notifier.Published.Single().Mutation.EntityId.Should().Be(card.Id);
    }

    [Fact]
    public async Task ExecuteProposal_ShouldStayApplied_WhenTheNotificationChannelThrowsAfterCommit()
    {
        // The catch inside FlushDeferredNotificationsAsync is load-bearing. Without it a throwing
        // notifier unwinds into the executor's outer catch, which rolls back an already-committed
        // transaction (a no-op) and then flips the Applied proposal to Failed - reporting failure
        // for a board change that really happened. This pins that it cannot.
        var throwingNotifier = new ThrowingBoardRealtimeNotifier();
        var executor = new AutomationExecutorService(
            _unitOfWorkMock.Object,
            _proposalServiceMock.Object,
            _policyEngineMock.Object,
            new CardService(_unitOfWorkMock.Object, throwingNotifier),
            new BoardService(_unitOfWorkMock.Object),
            new ColumnService(_unitOfWorkMock.Object),
            logger: null,
            assignments: null,
            realtimeNotifier: throwingNotifier);

        var (board, column, card) = SeedBoard(archived: false);
        var proposalId = Guid.NewGuid();
        var operations = new List<ProposalOperationDto>
        {
            LifecycleOperation(proposalId, sequence: 0, card, archive: true)
        };
        var entity = ArrangeApprovedProposal(proposalId, board.Id, operations);

        var result = await executor.ExecuteProposalAsync(proposalId, "execution-key");

        result.IsSuccess.Should().BeTrue("the board change committed; a dead notification channel is not a failure");
        throwingNotifier.Attempts.Should().Be(1, "the flush really did reach the channel");
        _committed.Should().BeTrue();
        _rolledBack.Should().BeFalse("there is nothing to roll back after a commit");
        entity.Status.Should().Be(ProposalStatus.Applied);
        _unitOfWorkMock.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private (Board Board, Column Column, Card Card) SeedBoard(bool archived)
    {
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id);
        var card = TestDataBuilder.CreateCard(board.Id, column.Id);
        if (archived)
            card.Archive();

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, It.IsAny<CancellationToken>())).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, It.IsAny<CancellationToken>())).ReturnsAsync(column);
        _cardRepoMock.Setup(r => r.GetByIdAsync(card.Id, It.IsAny<CancellationToken>())).ReturnsAsync(card);
        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card.Id, It.IsAny<CancellationToken>())).ReturnsAsync(card);
        return (board, column, card);
    }

    private static ProposalOperationDto LifecycleOperation(Guid proposalId, int sequence, Card card, bool archive)
        => new(
            Guid.NewGuid(),
            proposalId,
            sequence,
            archive ? "archive-lifecycle" : "restore-lifecycle",
            "card",
            null,
            $$"""{"cardId":"{{card.Id}}","expectedUpdatedAt":"{{card.UpdatedAt:O}}"}""",
            $"key-lifecycle-{sequence}",
            null);

    private AutomationProposal ArrangeApprovedProposal(Guid proposalId, Guid boardId, List<ProposalOperationDto> operations)
    {
        var userId = Guid.NewGuid();
        var proposal = new ProposalDto(
            proposalId,
            ProposalSourceType.Manual,
            null,
            boardId,
            userId,
            ProposalStatus.Approved,
            RiskLevel.Low,
            "Lifecycle proposal",
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow,
            Guid.NewGuid(),
            null,
            null,
            $"corr-{proposalId:N}",
            operations);

        var entity = new AutomationProposal(
            ProposalSourceType.Manual,
            userId,
            "Lifecycle proposal",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);
        entity.Approve(Guid.NewGuid());

        _proposalServiceMock.Setup(s => s.GetProposalByIdAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(proposal));
        _policyEngineMock.Setup(e => e.ValidatePolicy(proposal)).Returns(Result.Success());
        _policyEngineMock.Setup(e => e.ValidatePermissionsAsync(
                userId, boardId, operations, BoardAccessBar.Write, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _proposalRepoMock.Setup(r => r.GetByIdAsync(proposalId, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        return entity;
    }

    /// <summary>
    /// Captures each published event together with whether the unit of work had already committed
    /// at that moment — the ordering assertion the issue asks for.
    /// </summary>
    private sealed class RecordingBoardRealtimeNotifier : IBoardRealtimeNotifier
    {
        private readonly Func<bool> _committedProbe;
        private readonly List<(BoardRealtimeEvent Mutation, bool CommittedAtPublishTime)> _published = new();

        public RecordingBoardRealtimeNotifier(Func<bool> committedProbe) => _committedProbe = committedProbe;

        public IReadOnlyList<(BoardRealtimeEvent Mutation, bool CommittedAtPublishTime)> Published => _published;

        public Task NotifyBoardMutationAsync(BoardRealtimeEvent mutation, CancellationToken cancellationToken = default)
        {
            _published.Add((mutation, _committedProbe()));
            return Task.CompletedTask;
        }
    }

    /// <summary>A notification channel that is simply down — the shape the executor must survive.</summary>
    private sealed class ThrowingBoardRealtimeNotifier : IBoardRealtimeNotifier
    {
        public int Attempts { get; private set; }

        public Task NotifyBoardMutationAsync(BoardRealtimeEvent mutation, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new InvalidOperationException("realtime channel unavailable");
        }
    }
}
