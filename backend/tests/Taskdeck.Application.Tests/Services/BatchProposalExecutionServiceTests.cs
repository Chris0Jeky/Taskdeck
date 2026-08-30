using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Per-proposal batch execute (#1307, q-14 C). These cover the orchestration contract: item
/// isolation, per-item authorization, the approved-revision drift gate, and the fact that every
/// item reaches the SAME executor call single execute uses. Board-write behaviour itself belongs to
/// <see cref="AutomationExecutorServiceTests"/> and the API-level suite.
/// </summary>
public class BatchProposalExecutionServiceTests
{
    private readonly Mock<IProposalExecutionAuthorizationSnapshotReader> _snapshotReader = new();
    private readonly Mock<IAutomationExecutorService> _executorService = new();
    private readonly Mock<IAuthorizationService> _authorizationService = new();
    private readonly BatchProposalExecutionService _service;

    private readonly Guid _callerId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();

    public BatchProposalExecutionServiceTests()
    {
        _authorizationService
            .Setup(a => a.GetWritableBoardIdsAsync(
                It.IsAny<Guid>(),
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IEnumerable<Guid> boardIds, CancellationToken _) =>
                Result.Success<IReadOnlySet<Guid>>(boardIds.ToHashSet()));
        _authorizationService
            .Setup(a => a.GetReadableBoardIdsAsync(
                It.IsAny<Guid>(),
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IEnumerable<Guid> boardIds, CancellationToken _) =>
                Result.Success<IReadOnlySet<Guid>>(boardIds.ToHashSet()));

        _service = new BatchProposalExecutionService(
            _snapshotReader.Object,
            _executorService.Object,
            _authorizationService.Object);
    }

    [Fact]
    public async Task ExecuteProposals_WithNoSelections_ReturnsValidationError()
    {
        var result = await _service.ExecuteProposalsAsync(Array.Empty<BatchExecuteProposalSelectionDto>(), _callerId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ExecuteProposals_WhenOneItemFails_StillAppliesTheOthers()
    {
        var first = ArrangeProposal();
        var failing = ArrangeProposal();
        var third = ArrangeProposal();

        ArrangeExecute(first, new ProposalExecutionReceipt(AlreadyApplied: false, AppliedOperationCount: 2));
        _executorService
            .Setup(e => e.ExecuteProposalWithReceiptAsync(failing, It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProposalExecutionReceipt>(ErrorCodes.WipLimitExceeded, "WIP limit reached"));
        ArrangeExecute(third, new ProposalExecutionReceipt(AlreadyApplied: false, AppliedOperationCount: 1));

        var result = await _service.ExecuteProposalsAsync(
            new[] { Select(first), Select(failing), Select(third) },
            _callerId);

        result.IsSuccess.Should().BeTrue();
        var results = result.Value.Results;
        results.Should().HaveCount(3);
        results[0].Outcome.Should().Be(BatchExecuteOutcome.Applied);
        results[0].AppliedOperations.Should().Be(2);
        results[1].Outcome.Should().Be(BatchExecuteOutcome.Failed);
        results[1].ErrorCode.Should().Be(ErrorCodes.WipLimitExceeded);
        results[1].AppliedOperations.Should().BeNull();
        results[2].Outcome.Should().Be(BatchExecuteOutcome.Applied);
        results[2].AppliedOperations.Should().Be(1);

        // The failing item must not have short-circuited the ones after it: each proposal is its
        // own transaction, so the executor is still asked for every selected proposal.
        _executorService.Verify(
            e => e.ExecuteProposalWithReceiptAsync(third, It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteProposals_ReportsAlreadyAppliedProposalAsSkipped()
    {
        var replayed = ArrangeProposal();
        ArrangeExecute(replayed, new ProposalExecutionReceipt(AlreadyApplied: true, AppliedOperationCount: 0));

        var result = await _service.ExecuteProposalsAsync(new[] { Select(replayed) }, _callerId);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value.Results.Single();
        item.Outcome.Should().Be(BatchExecuteOutcome.Skipped);
        item.ErrorCode.Should().BeNull();
        // A replay did not apply operations on THIS call, so it must not claim a count.
        item.AppliedOperations.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteProposals_WhenProposalIsAppliedAfterPhaseOneSnapshot_ReportsSkippedFromFreshExecutorReceipt()
    {
        var proposal = ArrangeProposal();
        var concurrentApplyStaged = false;
        _authorizationService
            .Setup(a => a.GetReadableBoardIdsAsync(
                _callerId,
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IEnumerable<Guid> boardIds, CancellationToken _) =>
            {
                concurrentApplyStaged = true;
                return Result.Success<IReadOnlySet<Guid>>(boardIds.ToHashSet());
            });
        _executorService
            .Setup(e => e.ExecuteProposalWithReceiptAsync(
                proposal,
                It.IsAny<string>(),
                _callerId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                concurrentApplyStaged.Should().BeTrue(
                    "the competing Apply is staged after phase one and before this item's turn");
                return Result.Success(new ProposalExecutionReceipt(
                    AlreadyApplied: true,
                    AppliedOperationCount: 0));
            });

        var result = await _service.ExecuteProposalsAsync(new[] { Select(proposal) }, _callerId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Results.Should().ContainSingle().Which.Outcome.Should().Be(BatchExecuteOutcome.Skipped);
        _snapshotReader.Verify(
            reader => reader.FindAsync(proposal, It.IsAny<CancellationToken>()),
            Times.Once);
        _executorService.Verify(
            executor => executor.ExecuteProposalWithReceiptAsync(
                proposal,
                It.IsAny<string>(),
                _callerId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteProposals_PassesEachSelectionsOwnIdempotencyKeyThrough()
    {
        var first = ArrangeProposal();
        var second = ArrangeProposal();
        ArrangeExecute(first, new ProposalExecutionReceipt(false, 1));
        ArrangeExecute(second, new ProposalExecutionReceipt(false, 1));

        await _service.ExecuteProposalsAsync(
            new[]
            {
                new BatchExecuteProposalSelectionDto(first, null, "key-first"),
                new BatchExecuteProposalSelectionDto(second, null, "key-second"),
            },
            _callerId);

        _executorService.Verify(
            e => e.ExecuteProposalWithReceiptAsync(first, "key-first", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _executorService.Verify(
            e => e.ExecuteProposalWithReceiptAsync(second, "key-second", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteProposals_WhenCallerCannotWriteBoard_FailsOnlyThatItem()
    {
        var forbiddenBoardId = Guid.NewGuid();
        var allowed = ArrangeProposal();
        var forbidden = ArrangeProposal(boardId: forbiddenBoardId);
        ArrangeExecute(allowed, new ProposalExecutionReceipt(false, 1));
        // Writable: only the caller's own board. Readable: BOTH - so the forbidden item is on a
        // board the caller can see, which is the case where naming the real reason discloses nothing.
        _authorizationService
            .Setup(a => a.GetWritableBoardIdsAsync(
                _callerId,
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid> { _boardId }));

        var result = await _service.ExecuteProposalsAsync(
            new[] { Select(allowed), Select(forbidden) },
            _callerId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Results[0].Outcome.Should().Be(BatchExecuteOutcome.Applied);
        result.Value.Results[1].Outcome.Should().Be(BatchExecuteOutcome.Failed);
        result.Value.Results[1].ErrorCode.Should().Be(ErrorCodes.Forbidden);

        // The unauthorized item must never reach the executor: a 403-class outcome is decided
        // before any board write is attempted.
        _executorService.Verify(
            e => e.ExecuteProposalWithReceiptAsync(forbidden, It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteProposals_WhenBoardlessProposalBelongsToAnotherUser_FailsThatItemAsNotFound()
    {
        var foreign = ArrangeProposal(requestedByUserId: Guid.NewGuid(), useDefaultBoard: false);

        var result = await _service.ExecuteProposalsAsync(new[] { Select(foreign) }, _callerId);

        // NotFound, not Forbidden: a board-less proposal has no ACL that could make it visible, so
        // to anyone but its author it must be indistinguishable from one that does not exist.
        result.Value.Results.Single().Outcome.Should().Be(BatchExecuteOutcome.Failed);
        result.Value.Results.Single().ErrorCode.Should().Be(ErrorCodes.NotFound);
        _executorService.Verify(
            e => e.ExecuteProposalWithReceiptAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteProposals_WhenApprovedRevisionPinChanged_FailsThatItemWithConflict()
    {
        var drifted = ArrangeProposal(approvedRevisionId: Guid.NewGuid());

        // The reviewer echoed the pin they saw; the server now holds a different one.
        var result = await _service.ExecuteProposalsAsync(
            new[] { new BatchExecuteProposalSelectionDto(drifted, Guid.NewGuid(), "key") },
            _callerId);

        result.Value.Results.Single().Outcome.Should().Be(BatchExecuteOutcome.Failed);
        result.Value.Results.Single().ErrorCode.Should().Be(ErrorCodes.Conflict);
        _executorService.Verify(
            e => e.ExecuteProposalWithReceiptAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteProposals_WhenEchoedPinIsNullButProposalIsPinned_FailsThatItemWithConflict()
    {
        // The dangerous direction: a client that omits or nulls the pin must not be waved through
        // onto a proposal whose approved content was revised.
        var pinned = ArrangeProposal(approvedRevisionId: Guid.NewGuid());

        var result = await _service.ExecuteProposalsAsync(
            new[] { new BatchExecuteProposalSelectionDto(pinned, null, "key") },
            _callerId);

        result.Value.Results.Single().ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task ExecuteProposals_WhenPinMatches_ExecutesTheItem()
    {
        var revisionId = Guid.NewGuid();
        var pinned = ArrangeProposal(approvedRevisionId: revisionId);
        ArrangeExecute(pinned, new ProposalExecutionReceipt(false, 3));

        var result = await _service.ExecuteProposalsAsync(
            new[] { new BatchExecuteProposalSelectionDto(pinned, revisionId, "key") },
            _callerId);

        result.Value.Results.Single().Outcome.Should().Be(BatchExecuteOutcome.Applied);
        result.Value.Results.Single().AppliedOperations.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteProposals_WhenProposalIsMissing_FailsThatItemWithNotFound()
    {
        var missing = Guid.NewGuid();
        _snapshotReader
            .Setup(reader => reader.FindAsync(missing, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProposalExecutionAuthorizationSnapshot?)null);
        var present = ArrangeProposal();
        ArrangeExecute(present, new ProposalExecutionReceipt(false, 1));

        var result = await _service.ExecuteProposalsAsync(
            new[] { new BatchExecuteProposalSelectionDto(missing, null, "key"), Select(present) },
            _callerId);

        result.Value.Results[0].Outcome.Should().Be(BatchExecuteOutcome.Failed);
        result.Value.Results[0].ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Value.Results[1].Outcome.Should().Be(BatchExecuteOutcome.Applied);
    }

    [Fact]
    public async Task ExecuteProposals_WhenAclLookupFails_ReturnsWholeRequestFailureWithoutExecuting()
    {
        var proposal = ArrangeProposal();
        _authorizationService
            .Setup(a => a.GetWritableBoardIdsAsync(
                It.IsAny<Guid>(),
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlySet<Guid>>(ErrorCodes.UnexpectedError, "ACL read failed"));

        var result = await _service.ExecuteProposalsAsync(new[] { Select(proposal) }, _callerId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnexpectedError);
        _executorService.Verify(
            e => e.ExecuteProposalWithReceiptAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteProposals_ReadsEachDistinctBoardsAclExactlyOnce()
    {
        var first = ArrangeProposal();
        var second = ArrangeProposal();
        ArrangeExecute(first, new ProposalExecutionReceipt(false, 1));
        ArrangeExecute(second, new ProposalExecutionReceipt(false, 1));

        await _service.ExecuteProposalsAsync(new[] { Select(first), Select(second) }, _callerId);

        _authorizationService.Verify(
            a => a.GetWritableBoardIdsAsync(
                _callerId,
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteProposals_OnAnUnreadableBoard_IsIndistinguishableFromAMissingProposal()
    {
        // The enumeration guard. A caller who probes ids they have no access to must not be able to
        // tell a real foreign proposal from a made-up id, or a 500-item batch becomes a one-request
        // oracle over other people's proposals.
        var foreignBoardId = Guid.NewGuid();
        var foreign = ArrangeProposal(boardId: foreignBoardId);
        var missing = Guid.NewGuid();
        _snapshotReader
            .Setup(reader => reader.FindAsync(missing, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProposalExecutionAuthorizationSnapshot?)null);

        // Neither writable nor readable: the caller has no access to that board at all.
        _authorizationService
            .Setup(a => a.GetWritableBoardIdsAsync(_callerId, It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid>()));
        _authorizationService
            .Setup(a => a.GetReadableBoardIdsAsync(_callerId, It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid>()));

        var result = await _service.ExecuteProposalsAsync(
            new[] { Select(foreign), new BatchExecuteProposalSelectionDto(missing, null, "key") },
            _callerId);

        var existingRow = result.Value.Results[0];
        var missingRow = result.Value.Results[1];
        existingRow.Outcome.Should().Be(BatchExecuteOutcome.Failed);
        existingRow.ErrorCode.Should().Be(ErrorCodes.NotFound);
        // Byte-identical: code AND message. A different wording would leak the distinction the
        // code hides.
        existingRow.ErrorCode.Should().Be(missingRow.ErrorCode);
        existingRow.ErrorMessage.Should().Be(missingRow.ErrorMessage);
        existingRow.ErrorMessage.Should().NotContain(foreign.ToString());
    }

    [Fact]
    public async Task ExecuteProposals_OnAReadableButNotWritableBoard_StillReportsForbidden()
    {
        // The other half of the rule: the caller can already SEE this board, so its proposals'
        // existence is not news and the honest reason is the useful one.
        var readOnlyBoardId = Guid.NewGuid();
        var proposal = ArrangeProposal(boardId: readOnlyBoardId);
        _authorizationService
            .Setup(a => a.GetWritableBoardIdsAsync(_callerId, It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid>()));
        _authorizationService
            .Setup(a => a.GetReadableBoardIdsAsync(_callerId, It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid> { readOnlyBoardId }));

        var result = await _service.ExecuteProposalsAsync(new[] { Select(proposal) }, _callerId);

        result.Value.Results.Single().ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ExecuteProposals_CarriesTheCallerIntoEveryExecutorCall()
    {
        // Without this the executor can only recheck the proposal's original REQUESTER, and the
        // pre-loop authorization snapshot becomes the only thing standing between a revoked
        // collaborator and the rest of the batch.
        var first = ArrangeProposal();
        var second = ArrangeProposal();
        ArrangeExecute(first, new ProposalExecutionReceipt(false, 1));
        ArrangeExecute(second, new ProposalExecutionReceipt(false, 1));

        await _service.ExecuteProposalsAsync(new[] { Select(first), Select(second) }, _callerId);

        _executorService.Verify(
            e => e.ExecuteProposalWithReceiptAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                _callerId,
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteProposals_WhenReadableAclLookupFails_ReturnsWholeRequestFailure()
    {
        var proposal = ArrangeProposal();
        _authorizationService
            .Setup(a => a.GetReadableBoardIdsAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlySet<Guid>>(ErrorCodes.UnexpectedError, "ACL read failed"));

        var result = await _service.ExecuteProposalsAsync(new[] { Select(proposal) }, _callerId);

        result.IsSuccess.Should().BeFalse();
        _executorService.Verify(
            e => e.ExecuteProposalWithReceiptAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private BatchExecuteProposalSelectionDto Select(Guid proposalId) =>
        new(proposalId, ProposalPin(proposalId), Guid.NewGuid().ToString("N"));

    private readonly Dictionary<Guid, Guid?> _pins = new();

    private Guid? ProposalPin(Guid proposalId) => _pins.TryGetValue(proposalId, out var pin) ? pin : null;

    private Guid ArrangeProposal(
        Guid? boardId = null,
        Guid? requestedByUserId = null,
        Guid? approvedRevisionId = null,
        bool useDefaultBoard = true)
    {
        var proposalId = Guid.NewGuid();
        var resolvedBoardId = boardId ?? (useDefaultBoard ? _boardId : (Guid?)null);

        var snapshot = new ProposalExecutionAuthorizationSnapshot(
            proposalId,
            resolvedBoardId,
            requestedByUserId ?? _callerId,
            approvedRevisionId);

        _pins[proposalId] = approvedRevisionId;
        _snapshotReader
            .Setup(reader => reader.FindAsync(proposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        return proposalId;
    }

    private void ArrangeExecute(Guid proposalId, ProposalExecutionReceipt receipt) =>
        _executorService
            .Setup(e => e.ExecuteProposalWithReceiptAsync(proposalId, It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(receipt));
}
