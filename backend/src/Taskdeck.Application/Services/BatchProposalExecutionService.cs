using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <inheritdoc cref="IBatchProposalExecutionService"/>
public sealed class BatchProposalExecutionService : IBatchProposalExecutionService
{
    private readonly IProposalExecutionAuthorizationSnapshotReader _snapshotReader;
    private readonly IAutomationExecutorService _executorService;
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<BatchProposalExecutionService>? _logger;

    public BatchProposalExecutionService(
        IProposalExecutionAuthorizationSnapshotReader snapshotReader,
        IAutomationExecutorService executorService,
        IAuthorizationService authorizationService)
        : this(snapshotReader, executorService, authorizationService, logger: null)
    {
    }

    public BatchProposalExecutionService(
        IProposalExecutionAuthorizationSnapshotReader snapshotReader,
        IAutomationExecutorService executorService,
        IAuthorizationService authorizationService,
        ILogger<BatchProposalExecutionService>? logger)
    {
        _snapshotReader = snapshotReader;
        _executorService = executorService;
        _authorizationService = authorizationService;
        _logger = logger;
    }

    public async Task<Result<BatchExecuteProposalsResultDto>> ExecuteProposalsAsync(
        IReadOnlyList<BatchExecuteProposalSelectionDto> selections,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        if (selections is null || selections.Count == 0)
        {
            return Result.Failure<BatchExecuteProposalsResultDto>(
                ErrorCodes.ValidationError,
                "At least one proposal is required");
        }

        // Phase 1 - resolve only the authorization/pin fields through an untracked projection.
        // Keeping proposal entities out of this request's change tracker is load-bearing: another
        // request may apply an item while this batch is still preloading, and the executor's later
        // per-item status/idempotency lookup must see that persisted Apply rather than a stale
        // Approved entity from here. A resolution failure is that item's outcome; it never aborts
        // the batch, because one stale review selection must not block legitimate neighbours.
        var proposals = new Dictionary<Guid, ProposalExecutionAuthorizationSnapshot>();
        var resolutionFailures = new Dictionary<Guid, (string ErrorCode, string ErrorMessage)>();
        foreach (var selection in selections)
        {
            if (proposals.ContainsKey(selection.ProposalId) ||
                resolutionFailures.ContainsKey(selection.ProposalId))
            {
                continue;
            }

            var proposal = await _snapshotReader.FindAsync(selection.ProposalId, cancellationToken);
            if (proposal is not null)
                proposals[selection.ProposalId] = proposal;
            else
                resolutionFailures[selection.ProposalId] = (
                    ErrorCodes.NotFound,
                    $"Proposal with ID {selection.ProposalId} not found");
        }

        // Phase 2 - one batched ACL read for every distinct board. Single execute asks
        // CanWriteBoardAsync per proposal; asking once per distinct board admits exactly the same
        // set without an N+1 of a board fetch plus a membership read per selected item.
        var boardIds = proposals.Values
            .Where(proposal => proposal.BoardId.HasValue)
            .Select(proposal => proposal.BoardId!.Value)
            .ToHashSet();

        IReadOnlySet<Guid> writableBoardIds = new HashSet<Guid>();
        IReadOnlySet<Guid> readableBoardIds = new HashSet<Guid>();
        if (boardIds.Count > 0)
        {
            var writableResult = await _authorizationService.GetWritableBoardIdsAsync(
                callerUserId,
                boardIds,
                cancellationToken);
            if (!writableResult.IsSuccess)
            {
                // An ACL lookup that could not be answered is not a per-item verdict: failing items
                // open would apply board writes on an unknown permission, and failing them closed
                // would report a definite Forbidden the server never established.
                return Result.Failure<BatchExecuteProposalsResultDto>(
                    writableResult.ErrorCode,
                    writableResult.ErrorMessage);
            }

            writableBoardIds = writableResult.Value;

            // The readable set exists only to decide which of two failure SHAPES an inaccessible
            // item gets - see ExecuteOneAsync. This is authorization for disclosure, not for the
            // write; nothing below ever executes on the strength of readability.
            var readableResult = await _authorizationService.GetReadableBoardIdsAsync(
                callerUserId,
                boardIds,
                cancellationToken);
            if (!readableResult.IsSuccess)
            {
                return Result.Failure<BatchExecuteProposalsResultDto>(
                    readableResult.ErrorCode,
                    readableResult.ErrorMessage);
            }

            readableBoardIds = readableResult.Value;
        }

        // Phase 3 - execute sequentially, each item in its own transaction inside the executor.
        var results = new List<BatchExecuteProposalResultDto>(selections.Count);
        foreach (var selection in selections)
        {
            results.Add(await ExecuteOneAsync(
                selection,
                callerUserId,
                proposals,
                resolutionFailures,
                writableBoardIds,
                readableBoardIds,
                cancellationToken));
        }

        return Result.Success(new BatchExecuteProposalsResultDto(results));
    }

    private async Task<BatchExecuteProposalResultDto> ExecuteOneAsync(
        BatchExecuteProposalSelectionDto selection,
        Guid callerUserId,
        IReadOnlyDictionary<Guid, ProposalExecutionAuthorizationSnapshot> proposals,
        IReadOnlyDictionary<Guid, (string ErrorCode, string ErrorMessage)> resolutionFailures,
        IReadOnlySet<Guid> writableBoardIds,
        IReadOnlySet<Guid> readableBoardIds,
        CancellationToken cancellationToken)
    {
        if (resolutionFailures.TryGetValue(selection.ProposalId, out var resolutionFailure))
        {
            // Normalize the lookup's own NotFound to the shared wording, so a genuinely missing
            // proposal is byte-identical to one the caller may not see.
            return resolutionFailure.ErrorCode == ErrorCodes.NotFound
                ? NotFound(selection.ProposalId)
                : Failed(selection.ProposalId, resolutionFailure.ErrorCode, resolutionFailure.ErrorMessage);
        }

        if (!proposals.TryGetValue(selection.ProposalId, out var proposal))
            return NotFound(selection.ProposalId);

        // Same board-access bar as single execute - write access on the target board, or ownership
        // for a board-less proposal - applied per item, so a caller who cannot execute one item gets
        // that item's own failure row rather than a whole-request rejection.
        //
        // Which failure row depends on what the caller may already SEE, because a batch is an
        // efficient oracle otherwise: 500 guessed ids in one request would separate "exists but is
        // not yours" from "does not exist" in a single round trip, enumerating other people's
        // proposals. So an item on a board the caller cannot even read is reported exactly as a
        // missing one - same code, same message, and neither reaches the executor. Response TIMING
        // was not measured: the two paths do differ in work done (a resolved proposal has been read
        // from the store, an unknown id has not), so a timing side channel is not ruled out here.
        // Forbidden is reserved for a board the caller CAN read: they already
        // know that proposal exists, so naming the real reason discloses nothing and telling them
        // "not found" about a row on a board in front of them would be a lie.
        if (proposal.BoardId is Guid boardId)
        {
            if (!writableBoardIds.Contains(boardId))
            {
                return readableBoardIds.Contains(boardId)
                    ? Failed(
                        selection.ProposalId,
                        ErrorCodes.Forbidden,
                        "You do not have permission to modify this board")
                    : NotFound(selection.ProposalId);
            }
        }
        else if (proposal.RequestedByUserId != callerUserId)
        {
            // A board-less proposal has no ACL that could make it visible, so there is no
            // "readable" middle ground: to anyone but its author it does not exist.
            return NotFound(selection.ProposalId);
        }

        // Fail closed on approved-content drift. The reviewer consented to the pin the queue showed
        // them; if the proposal now pins a different revision (or none), apply that consent to
        // nothing rather than to content they never saw.
        if (proposal.ApprovedRevisionId != selection.ApprovedRevisionId)
        {
            return Failed(
                selection.ProposalId,
                ErrorCodes.Conflict,
                "The approved revision changed since this proposal was selected. Review it again.");
        }

        // callerUserId travels with the call so the executor can recheck THIS caller's board-write
        // bar inside the item's own transaction. The snapshot above was taken before the loop; a
        // revocation landing mid-batch must stop every remaining item, and only a transactional
        // recheck can see it.
        var receipt = await _executorService.ExecuteProposalWithReceiptAsync(
            selection.ProposalId,
            selection.IdempotencyKey,
            callerUserId,
            new ProposalExecutionRevisionExpectation(selection.ApprovedRevisionId),
            cancellationToken);
        if (!receipt.IsSuccess)
        {
            _logger?.LogWarning(
                "Batch execute item failed for proposal {ProposalId}: {ErrorCode} {ErrorMessage}",
                selection.ProposalId,
                receipt.ErrorCode,
                receipt.ErrorMessage);
            return Failed(selection.ProposalId, receipt.ErrorCode, receipt.ErrorMessage);
        }

        return receipt.Value.AlreadyApplied
            ? new BatchExecuteProposalResultDto(
                selection.ProposalId,
                BatchExecuteOutcome.Skipped,
                ErrorCode: null,
                ErrorMessage: null,
                AppliedOperations: null)
            : new BatchExecuteProposalResultDto(
                selection.ProposalId,
                BatchExecuteOutcome.Applied,
                ErrorCode: null,
                ErrorMessage: null,
                AppliedOperations: receipt.Value.AppliedOperationCount);
    }

    private static BatchExecuteProposalResultDto Failed(Guid proposalId, string errorCode, string errorMessage) =>
        new(proposalId, BatchExecuteOutcome.Failed, errorCode, errorMessage, AppliedOperations: null);

    /// <summary>
    /// The single not-found shape, used for a proposal that does not exist AND for one the caller
    /// has no read access to. The message deliberately carries no detail that would distinguish the
    /// two - not even the id's existence - because telling them apart is the whole enumeration
    /// attack a 500-item batch would otherwise automate.
    /// </summary>
    private static BatchExecuteProposalResultDto NotFound(Guid proposalId) =>
        Failed(proposalId, ErrorCodes.NotFound, "Proposal not found.");
}
