using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class AutomationExecutorService : IAutomationExecutorService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAutomationProposalService _proposalService;
    private readonly IAutomationPolicyEngine _policyEngine;
    private readonly OperationHandlerRegistry _handlerRegistry;
    private readonly ExecutionAuditRecorder _auditRecorder;
    private readonly ILogger<AutomationExecutorService>? _logger;

    public AutomationExecutorService(
        IUnitOfWork unitOfWork,
        IAutomationProposalService proposalService,
        IAutomationPolicyEngine policyEngine,
        CardService cardService,
        BoardService boardService,
        ColumnService columnService)
        : this(unitOfWork, proposalService, policyEngine, cardService, boardService, columnService, logger: null)
    {
    }

    public AutomationExecutorService(
        IUnitOfWork unitOfWork,
        IAutomationProposalService proposalService,
        IAutomationPolicyEngine policyEngine,
        CardService cardService,
        BoardService boardService,
        ColumnService columnService,
        ILogger<AutomationExecutorService>? logger)
    {
        _unitOfWork = unitOfWork;
        _proposalService = proposalService;
        _policyEngine = policyEngine;
        _handlerRegistry = new OperationHandlerRegistry(unitOfWork, cardService, boardService, columnService);
        _auditRecorder = new ExecutionAuditRecorder(unitOfWork);
        _logger = logger;
    }

    public async Task<Result> ExecuteProposalAsync(Guid proposalId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // Thin projection of the receipt call: one execution path, one materialization, one set of
        // guards. Callers that only need success/failure keep their existing signature.
        var receipt = await ExecuteProposalWithReceiptAsync(
            proposalId,
            idempotencyKey,
            callerUserId: null,
            cancellationToken);
        return receipt.IsSuccess ? Result.Success() : Result.Failure(receipt.ErrorCode, receipt.ErrorMessage);
    }

    public Task<Result<ProposalExecutionReceipt>> ExecuteProposalWithReceiptAsync(
        Guid proposalId,
        string idempotencyKey,
        Guid? callerUserId = null,
        CancellationToken cancellationToken = default) =>
        ExecuteProposalWithReceiptCoreAsync(
            proposalId,
            idempotencyKey,
            callerUserId,
            revisionExpectation: null,
            cancellationToken);

    public Task<Result<ProposalExecutionReceipt>> ExecuteProposalWithReceiptAsync(
        Guid proposalId,
        string idempotencyKey,
        Guid? callerUserId,
        ProposalExecutionRevisionExpectation revisionExpectation,
        CancellationToken cancellationToken = default) =>
        ExecuteProposalWithReceiptCoreAsync(
            proposalId,
            idempotencyKey,
            callerUserId,
            revisionExpectation,
            cancellationToken);

    private async Task<Result<ProposalExecutionReceipt>> ExecuteProposalWithReceiptCoreAsync(
        Guid proposalId,
        string idempotencyKey,
        Guid? callerUserId,
        ProposalExecutionRevisionExpectation? revisionExpectation,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        if (proposalId == Guid.Empty)
        {
            _logger?.LogWarning("Automation proposal execution rejected: empty proposalId");
            return Result.Failure<ProposalExecutionReceipt>(ErrorCodes.ValidationError, "ProposalId cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _logger?.LogWarning("Automation proposal execution rejected for proposal {ProposalId}: missing idempotency key", proposalId);
            return Result.Failure<ProposalExecutionReceipt>(ErrorCodes.ValidationError, "IdempotencyKey cannot be empty");
        }

        // Get proposal
        var proposalResult = await _proposalService.GetProposalByIdAsync(proposalId, cancellationToken);
        if (!proposalResult.IsSuccess)
        {
            _logger?.LogWarning(
                "Automation proposal execution failed for proposal {ProposalId} after {DurationMs}ms: {ErrorCode} {ErrorMessage}",
                proposalId,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                proposalResult.ErrorCode,
                proposalResult.ErrorMessage);
            return Result.Failure<ProposalExecutionReceipt>(proposalResult.ErrorCode, proposalResult.ErrorMessage);
        }

        var proposal = proposalResult.Value;

        // Batch ACL preload is only a phase-one disclosure/fast-fail snapshot. Recheck the human
        // caller against this freshly loaded proposal before status, idempotency, revision, or
        // linked-capture decisions. The existing in-transaction check below remains the final bar
        // for operations; this earlier bar closes the already-Applied path, which has no operation
        // transaction but can still write capture-sync metadata.
        if (callerUserId is Guid requestCallerId)
        {
            var callerPermission = proposal.BoardId.HasValue
                ? await _policyEngine.ValidateBoardAccessAsync(
                    requestCallerId,
                    proposal.BoardId,
                    BoardAccessBar.Write,
                    cancellationToken)
                : proposal.RequestedByUserId == requestCallerId
                    ? Result.Success()
                    : Result.Failure(
                        ErrorCodes.Forbidden,
                        "You do not have permission to access this proposal.");

            if (!callerPermission.IsSuccess)
            {
                _logger?.LogWarning(
                    "Automation proposal execution refused for proposal {ProposalId}: caller {CallerUserId} no longer has board write access",
                    proposalId,
                    requestCallerId);
                return Result.Failure<ProposalExecutionReceipt>(
                    callerPermission.ErrorCode,
                    callerPermission.ErrorMessage);
            }
        }

        // The expectation object itself distinguishes an explicit expected null pin from callers
        // that supplied no expectation (single execute). Compare it to this SAME fresh proposal
        // lookup before the already-applied sync and before any operation can run.
        if (revisionExpectation is not null &&
            proposal.ApprovedRevisionId != revisionExpectation.ApprovedRevisionId)
        {
            return Result.Failure<ProposalExecutionReceipt>(
                ErrorCodes.Conflict,
                "The approved revision changed since this proposal was selected. Review it again.");
        }

        // Idempotent behavior across requests/processes: already-applied proposals are treated as success.
        if (proposal.Status == ProposalStatus.Applied)
        {
            var syncResult = await SyncLinkedCaptureConversionAsync(proposal, cancellationToken);
            if (!syncResult.IsSuccess)
            {
                _logger?.LogWarning(
                    "Already-applied proposal {ProposalId} could not sync linked capture conversion: {ErrorCode} {ErrorMessage}",
                    proposalId,
                    syncResult.ErrorCode,
                    syncResult.ErrorMessage);
            }

            _logger?.LogInformation(
                "Automation proposal execution skipped for already-applied proposal {ProposalId} after {DurationMs}ms",
                proposalId,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            return Result.Success(new ProposalExecutionReceipt(AlreadyApplied: true, AppliedOperationCount: 0));
        }

        // Verify proposal is approved
        if (proposal.Status != ProposalStatus.Approved)
        {
            _logger?.LogWarning(
                "Automation proposal execution rejected for proposal {ProposalId} after {DurationMs}ms due to status {Status}",
                proposalId,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                proposal.Status);
            return Result.Failure<ProposalExecutionReceipt>(ErrorCodes.InvalidOperation, $"Cannot execute proposal in status {proposal.Status}");
        }

        var effectiveProposalResult = await MaterializeEffectiveProposalAsync(proposal, cancellationToken);
        if (!effectiveProposalResult.IsSuccess)
        {
            _logger?.LogWarning(
                "Automation proposal execution rejected for proposal {ProposalId} after {DurationMs}ms because revised payload is invalid: {ErrorCode} {ErrorMessage}",
                proposalId,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                effectiveProposalResult.ErrorCode,
                effectiveProposalResult.ErrorMessage);
            return Result.Failure<ProposalExecutionReceipt>(effectiveProposalResult.ErrorCode, effectiveProposalResult.ErrorMessage);
        }

        var effectiveProposal = effectiveProposalResult.Value;

        // Revalidate policy before execution
        var policyResult = _policyEngine.ValidatePolicy(effectiveProposal);
        if (!policyResult.IsSuccess)
        {
            _logger?.LogWarning(
                "Automation proposal execution policy validation failed for proposal {ProposalId} after {DurationMs}ms: {ErrorCode} {ErrorMessage}",
                proposalId,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                policyResult.ErrorCode,
                policyResult.ErrorMessage);
            return Result.Failure<ProposalExecutionReceipt>(policyResult.ErrorCode, policyResult.ErrorMessage);
        }

        // Revalidate permissions. Execute is the mutation lane par excellence, so the requester
        // must clear the write bar on the target board (#1836) — the same bar the API-side
        // #1794/#1827 AuthorizationService.CanWriteBoardAsync applies at the execute endpoint.
        var permissionResult = await _policyEngine.ValidatePermissionsAsync(
            effectiveProposal.RequestedByUserId,
            effectiveProposal.BoardId,
            effectiveProposal.Operations,
            BoardAccessBar.Write,
            cancellationToken);
        if (!permissionResult.IsSuccess)
        {
            _logger?.LogWarning(
                "Automation proposal execution permission validation failed for proposal {ProposalId} after {DurationMs}ms: {ErrorCode} {ErrorMessage}",
                proposalId,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                permissionResult.ErrorCode,
                permissionResult.ErrorMessage);
            return Result.Failure<ProposalExecutionReceipt>(permissionResult.ErrorCode, permissionResult.ErrorMessage);
        }

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var decisionGuard = await _policyEngine.GuardProposalDecisionWritesAsync(
                new[] { effectiveProposal.BoardId },
                cancellationToken);
            if (!decisionGuard.IsSuccess)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<ProposalExecutionReceipt>(decisionGuard.ErrorCode, decisionGuard.ErrorMessage);
            }

            // The caller's own bar, rechecked HERE - inside this item's transaction, after the
            // archive guard and before the first operation - not at the top of a batch. The
            // permission check above validates the proposal's original REQUESTER, which is a
            // different person from the submitter whenever a collaborator applies someone else's
            // proposal; and a batch reads authorization once before its loop, so a revocation that
            // lands mid-batch would otherwise be invisible to every remaining item.
            if (callerUserId is Guid callerId)
            {
                var callerPermission = effectiveProposal.BoardId.HasValue
                    ? await _policyEngine.ValidatePermissionsAsync(
                        callerId,
                        effectiveProposal.BoardId,
                        effectiveProposal.Operations,
                        BoardAccessBar.Write,
                        cancellationToken)
                    // A board-less proposal has no ACL to consult, so ownership is the only bar
                    // there is - the same rule the single-execute endpoint applies.
                    : effectiveProposal.RequestedByUserId == callerId
                        ? Result.Success()
                        : Result.Failure(
                            ErrorCodes.Forbidden,
                            "You do not have permission to access this proposal.");

                if (!callerPermission.IsSuccess)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    _logger?.LogWarning(
                        "Automation proposal execution refused for proposal {ProposalId}: caller {CallerUserId} lost board write access before execution",
                        proposalId,
                        callerId);
                    return Result.Failure<ProposalExecutionReceipt>(
                        callerPermission.ErrorCode,
                        callerPermission.ErrorMessage);
                }
            }

            // Execute operations in sequence order
            var orderedOperations = effectiveProposal.Operations.OrderBy(o => o.Sequence).ToList();
            var failedOperation = -1;
            var failedResult = Result.Success();
            var failureReason = "";

            foreach (var operation in orderedOperations)
            {
                var executionResult = await _handlerRegistry.ExecuteOperationAsync(operation, cancellationToken);
                if (!executionResult.IsSuccess)
                {
                    failedOperation = operation.Sequence;
                    failedResult = executionResult;
                    failureReason = $"Operation {operation.Sequence} ({operation.ActionType} {operation.TargetType}) failed: {executionResult.ErrorMessage}";
                    break;
                }

                // Create audit log for the operation
                await _auditRecorder.RecordAsync(operation, effectiveProposal, cancellationToken);
            }

            if (failedOperation >= 0)
            {
                // Mark proposal as failed and rollback transaction
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                // The failed status is a separate decision write after the operation transaction
                // rolls back, so it must load and guard the board again. If the operation itself
                // lost a race with archive, preserve that Conflict as the primary outcome even
                // when the now-archived board refuses the follow-up Failed write.
                var updateResult = await UpdateProposalStatusAsync(
                    proposalId,
                    ProposalStatus.Failed,
                    failureReason,
                    guardDecisionWrite: true,
                    cancellationToken);
                if (!updateResult.IsSuccess)
                {
                    return failedResult.ErrorCode == ErrorCodes.Conflict
                        ? Result.Failure<ProposalExecutionReceipt>(failedResult.ErrorCode, failureReason)
                        : Result.Failure<ProposalExecutionReceipt>(updateResult.ErrorCode, updateResult.ErrorMessage);
                }

                _logger?.LogWarning(
                    "Automation proposal execution failed for proposal {ProposalId} at operation {OperationSequence} after {DurationMs}ms: {FailureReason}",
                    proposalId,
                    failedOperation,
                    (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                    failureReason);
                return Result.Failure<ProposalExecutionReceipt>(failedResult.ErrorCode, failureReason);
            }

            // The board marker, operation effects, audit rows, and Applied status share this outer
            // transaction. Do not re-check archived state here: an approved operation may itself
            // archive the board, and the pre-operation guard already ordered that legitimate write.
            var markResult = await UpdateProposalStatusAsync(
                proposalId,
                ProposalStatus.Applied,
                null,
                guardDecisionWrite: false,
                cancellationToken);
            if (!markResult.IsSuccess)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<ProposalExecutionReceipt>(markResult.ErrorCode, markResult.ErrorMessage);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var captureSyncResult = await SyncLinkedCaptureConversionAsync(
                effectiveProposal with
                {
                    Status = ProposalStatus.Applied,
                    AppliedAt = DateTime.UtcNow
                },
                cancellationToken);
            if (!captureSyncResult.IsSuccess)
            {
                _logger?.LogWarning(
                    "Applied proposal {ProposalId} could not sync linked capture conversion: {ErrorCode} {ErrorMessage}",
                    proposalId,
                    captureSyncResult.ErrorCode,
                    captureSyncResult.ErrorMessage);
            }

            _logger?.LogInformation(
                "Automation proposal execution completed for proposal {ProposalId} in {DurationMs}ms with {OperationCount} operation(s)",
                proposalId,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                orderedOperations.Count);
            return Result.Success(new ProposalExecutionReceipt(
                AlreadyApplied: false,
                AppliedOperationCount: orderedOperations.Count));
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            await UpdateProposalStatusAsync(
                proposalId,
                ProposalStatus.Failed,
                ex.Message,
                guardDecisionWrite: true,
                cancellationToken);
            _logger?.LogError(
                ex,
                "Automation proposal execution threw for proposal {ProposalId} after {DurationMs}ms",
                proposalId,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            return Result.Failure<ProposalExecutionReceipt>(ErrorCodes.UnexpectedError, $"Failed to execute proposal: {ex.Message}");
        }
    }

    private async Task<Result<ProposalDto>> MaterializeEffectiveProposalAsync(
        ProposalDto proposal,
        CancellationToken cancellationToken)
    {
        // Apply materializes exactly the revision pinned at approve time (#1428), NOT the latest
        // one. A null pin means the proposal was approved from its original operations, so a
        // revision saved after approval — including one that landed in the race window between
        // approve's validation read and its commit — cannot change what Apply executes.
        if (proposal.ApprovedRevisionId is not Guid approvedRevisionId)
            return Result.Success(proposal);

        var pinnedRevision = await _unitOfWork.ProposalRevisions.GetByIdAsync(
            approvedRevisionId,
            cancellationToken);
        if (pinnedRevision is null)
        {
            // The pinned revision is cascade-owned by the proposal, so it can only vanish together
            // with the proposal itself — this branch is unreachable in practice. Refuse to apply
            // rather than silently fall back to the (unapproved) original operations. Shaped as
            // InvalidOperation (server invariant violation), NOT NotFound: the proposal being
            // executed exists, and a NotFound here would misread as "proposal not found".
            return Result.Failure<ProposalDto>(
                ErrorCodes.InvalidOperation,
                $"Server invariant violation: proposal {proposal.Id} pins approved revision " +
                $"{approvedRevisionId}, but that revision no longer exists; refusing to apply");
        }

        if (!ProposalRevisionPayload.TryParseOperations(
                proposal.Id,
                pinnedRevision.RevisedPayload,
                out var revisedOperations,
                out var errorMessage))
        {
            return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, errorMessage);
        }

        return Result.Success(proposal with { Operations = revisedOperations });
    }

    private async Task<Result> SyncLinkedCaptureConversionAsync(ProposalDto proposal, CancellationToken cancellationToken)
    {
        if (proposal.SourceType != ProposalSourceType.Queue ||
            string.IsNullOrWhiteSpace(proposal.SourceReferenceId) ||
            !Guid.TryParse(proposal.SourceReferenceId, out var sourceRequestId))
        {
            return Result.Success();
        }

        var captureItem = await _unitOfWork.LlmQueue.GetByIdAsync(sourceRequestId, cancellationToken);
        if (captureItem == null || !CaptureRequestContract.IsCaptureRequestType(captureItem.RequestType))
        {
            return Result.Success();
        }

        var payloadResult = CaptureRequestContract.ParsePayload(captureItem.Payload, allowServerAttributionFields: true);
        if (!payloadResult.IsSuccess)
        {
            _logger?.LogWarning(
                "Automation proposal {ProposalId} applied but linked capture item {CaptureItemId} payload could not be parsed for conversion sync: {ErrorCode} {ErrorMessage}",
                proposal.Id,
                captureItem.Id,
                payloadResult.ErrorCode,
                payloadResult.ErrorMessage);
            return Result.Success();
        }

        var provenance = payloadResult.Value.Provenance;
        if (captureItem.UserId != proposal.RequestedByUserId)
        {
            _logger?.LogWarning(
                "Automation proposal {ProposalId} skipped capture conversion sync because linked capture item {CaptureItemId} belongs to a different user",
                proposal.Id,
                captureItem.Id);
            return Result.Success();
        }

        if (provenance?.ProposalId is not { } linkedProposalId || linkedProposalId == Guid.Empty)
        {
            _logger?.LogWarning(
                "Automation proposal {ProposalId} skipped capture conversion sync because linked capture item {CaptureItemId} is not already attributed to this proposal",
                proposal.Id,
                captureItem.Id);
            return Result.Success();
        }

        if (linkedProposalId != proposal.Id)
        {
            _logger?.LogWarning(
                "Automation proposal {ProposalId} skipped capture conversion sync because linked capture item {CaptureItemId} already points at proposal {LinkedProposalId}",
                proposal.Id,
                captureItem.Id,
                linkedProposalId);
            return Result.Success();
        }

        if (provenance?.ConvertedAt is not null)
        {
            return Result.Success();
        }

        var resolvedBoardId = captureItem.BoardId ?? proposal.BoardId;
        var convertedAt = provenance?.ConvertedAt ?? CaptureConversionTimestamp.ResolveConvertedAt(proposal.AppliedAt);
        var updatedPayload = CaptureRequestContract.WithProvenance(
            payloadResult.Value,
            captureItem.Id,
            proposalId: proposal.Id,
            boardId: resolvedBoardId,
            convertedAt: convertedAt);

        try
        {
            if (!captureItem.BoardId.HasValue && resolvedBoardId.HasValue)
            {
                captureItem.BackfillBoard(resolvedBoardId.Value);
            }

            captureItem.UpdatePayload(CaptureRequestContract.SerializePayload(updatedPayload));
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure(ErrorCodes.UnexpectedError, ex.Message);
        }
    }

    private async Task<Result> UpdateProposalStatusAsync(
        Guid proposalId,
        ProposalStatus status,
        string? failureReason,
        bool guardDecisionWrite,
        CancellationToken cancellationToken)
    {
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(proposalId, cancellationToken);
        if (proposal == null)
            return Result.Failure(ErrorCodes.NotFound, $"Proposal with ID {proposalId} not found");

        try
        {
            if (guardDecisionWrite)
            {
                var decisionGuard = await _policyEngine.GuardProposalDecisionWritesAsync(
                    new[] { proposal.BoardId },
                    cancellationToken);
                if (!decisionGuard.IsSuccess)
                    return decisionGuard;
            }

            if (status == ProposalStatus.Applied)
            {
                proposal.MarkAsApplied();
            }
            else if (status == ProposalStatus.Failed)
            {
                proposal.MarkAsFailed(failureReason ?? "Unknown error");
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.ErrorCode, ex.Message);
        }
    }
}
