using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <inheritdoc cref="IBatchProposalExecutionService"/>
public sealed class BatchProposalExecutionService : IBatchProposalExecutionService
{
    private readonly IAutomationProposalService _proposalService;
    private readonly IAutomationExecutorService _executorService;
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<BatchProposalExecutionService>? _logger;

    public BatchProposalExecutionService(
        IAutomationProposalService proposalService,
        IAutomationExecutorService executorService,
        IAuthorizationService authorizationService)
        : this(proposalService, executorService, authorizationService, logger: null)
    {
    }

    public BatchProposalExecutionService(
        IAutomationProposalService proposalService,
        IAutomationExecutorService executorService,
        IAuthorizationService authorizationService,
        ILogger<BatchProposalExecutionService>? logger)
    {
        _proposalService = proposalService;
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

        // Phase 1 - resolve every selected proposal. A resolution failure is that item's outcome;
        // it never aborts the batch, because a stale row in one reviewer's selection must not block
        // the proposals they can legitimately apply.
        var proposals = new Dictionary<Guid, ProposalDto>();
        var resolutionFailures = new Dictionary<Guid, (string ErrorCode, string ErrorMessage)>();
        foreach (var selection in selections)
        {
            if (proposals.ContainsKey(selection.ProposalId) ||
                resolutionFailures.ContainsKey(selection.ProposalId))
            {
                continue;
            }

            var proposalResult = await _proposalService.GetProposalByIdAsync(selection.ProposalId, cancellationToken);
            if (proposalResult.IsSuccess)
                proposals[selection.ProposalId] = proposalResult.Value;
            else
                resolutionFailures[selection.ProposalId] = (proposalResult.ErrorCode, proposalResult.ErrorMessage);
        }

        // Phase 2 - one batched ACL read for every distinct board. Single execute asks
        // CanWriteBoardAsync per proposal; asking once per distinct board admits exactly the same
        // set without an N+1 of a board fetch plus a membership read per selected item.
        var boardIds = proposals.Values
            .Where(proposal => proposal.BoardId.HasValue)
            .Select(proposal => proposal.BoardId!.Value)
            .ToHashSet();

        IReadOnlySet<Guid> writableBoardIds = new HashSet<Guid>();
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
                cancellationToken));
        }

        return Result.Success(new BatchExecuteProposalsResultDto(results));
    }

    private async Task<BatchExecuteProposalResultDto> ExecuteOneAsync(
        BatchExecuteProposalSelectionDto selection,
        Guid callerUserId,
        IReadOnlyDictionary<Guid, ProposalDto> proposals,
        IReadOnlyDictionary<Guid, (string ErrorCode, string ErrorMessage)> resolutionFailures,
        IReadOnlySet<Guid> writableBoardIds,
        CancellationToken cancellationToken)
    {
        if (resolutionFailures.TryGetValue(selection.ProposalId, out var resolutionFailure))
            return Failed(selection.ProposalId, resolutionFailure.ErrorCode, resolutionFailure.ErrorMessage);

        if (!proposals.TryGetValue(selection.ProposalId, out var proposal))
        {
            return Failed(
                selection.ProposalId,
                ErrorCodes.NotFound,
                $"Proposal with ID {selection.ProposalId} not found");
        }

        // Same board-access bar as single execute: write access on the target board, or ownership
        // for a board-less proposal. A caller who cannot execute one item gets that item's own
        // 403-class outcome, not a whole-request rejection.
        if (proposal.BoardId is Guid boardId)
        {
            if (!writableBoardIds.Contains(boardId))
            {
                return Failed(
                    selection.ProposalId,
                    ErrorCodes.Forbidden,
                    "You do not have permission to modify this board");
            }
        }
        else if (proposal.RequestedByUserId != callerUserId)
        {
            return Failed(
                selection.ProposalId,
                ErrorCodes.Forbidden,
                "You do not have permission to access this proposal.");
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

        var receipt = await _executorService.ExecuteProposalWithReceiptAsync(
            selection.ProposalId,
            selection.IdempotencyKey,
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
}
