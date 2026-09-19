using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Production-facing proposal service that composes the host's late safety boundaries.
///
/// Proposal creation flows through <see cref="RelationProposalAdmissionService"/> so every producer
/// validates typed relations before persistence. Automatic expiry reads one authoritative candidate
/// snapshot, guards exactly that snapshot's boards, and consumes the same entities for mutation,
/// reporting and notifications so a threshold crossing cannot enter the save without a board guard.
/// All other lifecycle behaviour remains owned by the concrete service.
/// </summary>
public sealed class ProposalExpiryGuardedService(
    AutomationProposalService inner,
    IUnitOfWork unitOfWork,
    IAutomationPolicyEngine policyEngine,
    INotificationService? notificationService = null,
    ILogger<ProposalExpiryGuardedService>? logger = null) : IAutomationProposalService
{
    private readonly RelationProposalAdmissionService _admission = new(inner, policyEngine);
    private readonly INotificationService _notificationService = notificationService ?? NoOpNotificationService.Instance;
    private int _lastSkippedArchivedBoardCount;

    public Task<Result<ProposalDto>> CreateProposalAsync(
        CreateProposalDto dto,
        CancellationToken cancellationToken = default) =>
        _admission.CreateProposalAsync(dto, cancellationToken);

    public Task<Result<ProposalDto>> CreateTranscriptProposalAsync(
        CreateProposalDto dto,
        IReadOnlyList<TranscriptEvidenceLinkInput> evidence,
        CancellationToken cancellationToken = default) =>
        _admission.CreateTranscriptProposalAsync(dto, evidence, cancellationToken);

    public Task<Result<ProposalDto>> GetProposalByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        inner.GetProposalByIdAsync(id, cancellationToken);

    public Task<Result<IEnumerable<ProposalDto>>> GetProposalsAsync(
        ProposalFilterDto? filter = null,
        CancellationToken cancellationToken = default) =>
        inner.GetProposalsAsync(filter, cancellationToken);

    public Task<Result<ProposalDto>> ApproveProposalAsync(
        Guid id,
        Guid decidedByUserId,
        CancellationToken cancellationToken = default) =>
        inner.ApproveProposalAsync(id, decidedByUserId, cancellationToken);

    public Task<Result<BatchApproveProposalsResultDto>> ApproveProposalsAsync(
        IReadOnlyList<BatchApproveProposalSelectionDto> proposals,
        Guid decidedByUserId,
        CancellationToken cancellationToken = default) =>
        inner.ApproveProposalsAsync(proposals, decidedByUserId, cancellationToken);

    public Task<Result<ProposalDto>> RejectProposalAsync(
        Guid id,
        Guid decidedByUserId,
        UpdateProposalStatusDto dto,
        CancellationToken cancellationToken = default) =>
        inner.RejectProposalAsync(id, decidedByUserId, dto, cancellationToken);

    public Task<Result<ProposalDto>> DeferProposalAsync(
        Guid id,
        TimeSpan duration,
        CancellationToken cancellationToken = default) =>
        inner.DeferProposalAsync(id, duration, cancellationToken);

    public Task<Result<ProposalDto>> MarkAsAppliedAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        inner.MarkAsAppliedAsync(id, cancellationToken);

    public Task<Result<ProposalDto>> MarkAsFailedAsync(
        Guid id,
        string failureReason,
        CancellationToken cancellationToken = default) =>
        inner.MarkAsFailedAsync(id, failureReason, cancellationToken);

    public async Task<Result<int>> ExpireProposalsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // This is the ONE authoritative candidate read for the production service. The same
            // entity instances are guarded and then mutated; candidates that cross the threshold
            // after this read wait for the next sweep rather than joining an unguarded second read.
            var sweep = await unitOfWork.AutomationProposals.GetExpiredAsync(cancellationToken);
            var expiredProposals = sweep.Expirable;
            if (expiredProposals.Count > 0)
            {
                var guard = await policyEngine.GuardProposalDecisionWritesAsync(
                    expiredProposals.Select(proposal => proposal.BoardId),
                    cancellationToken);
                if (!guard.IsSuccess)
                    return Result.Failure<int>(guard.ErrorCode, guard.ErrorMessage);
            }

            ReportArchivedBoardPartition(sweep.SkippedArchivedBoardCount);

            foreach (var proposal in expiredProposals)
                proposal.Expire();

            if (expiredProposals.Count > 0)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);

                foreach (var proposal in expiredProposals)
                {
                    var notifyResult = await PublishProposalOutcomeNotificationAsync(
                        proposal,
                        cancellationToken);
                    if (!notifyResult.IsSuccess)
                        return Result.Failure<int>(notifyResult.ErrorCode, notifyResult.ErrorMessage);
                }
            }

            return Result.Success(expiredProposals.Count);
        }
        catch (DomainException exception)
        {
            return Result.Failure<int>(exception.ErrorCode, exception.Message);
        }
    }

    private void ReportArchivedBoardPartition(int skippedCount)
    {
        if (skippedCount != _lastSkippedArchivedBoardCount)
        {
            if (skippedCount > 0)
            {
                // Count only: no proposal id, summary or board name crosses the operator log.
                logger?.LogInformation(
                    "Skipped expiring {SkippedCount} stale proposals because their board is archived; "
                        + "restore the board to let them expire.",
                    skippedCount);
            }
            else
            {
                logger?.LogInformation(
                    "No stale proposals are being withheld for archived boards any more.");
            }
        }
        else if (skippedCount > 0)
        {
            logger?.LogDebug(
                "Still skipping {SkippedCount} stale proposals because their board is archived.",
                skippedCount);
        }

        _lastSkippedArchivedBoardCount = skippedCount;
    }

    private async Task<Result> PublishProposalOutcomeNotificationAsync(
        Taskdeck.Domain.Entities.AutomationProposal proposal,
        CancellationToken cancellationToken)
    {
        var publishResult = await _notificationService.PublishAsync(
            new CreateNotificationRequestDto(
                proposal.RequestedByUserId,
                NotificationType.ProposalOutcome,
                "Automation proposal updated",
                $"Your proposal '{proposal.Summary}' is now expired.",
                proposal.BoardId,
                SourceEntityType: "proposal",
                SourceEntityId: proposal.Id,
                DeduplicationKey: $"proposal:{proposal.Id}:{proposal.Status}"),
            cancellationToken);

        return publishResult.IsSuccess
            ? Result.Success()
            : Result.Failure(publishResult.ErrorCode, publishResult.ErrorMessage);
    }

    public Task<Result<string>> GetProposalDiffAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        inner.GetProposalDiffAsync(id, cancellationToken);

    public Task<Result<ProposalPreviewDto>> GetProposalPreviewAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        inner.GetProposalPreviewAsync(id, cancellationToken);

    public Task<Result<string>> GetTerminalProposalStoredPreviewAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        inner.GetTerminalProposalStoredPreviewAsync(id, cancellationToken);

    public Task<Result<int>> DismissProposalsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default) =>
        inner.DismissProposalsAsync(ids, cancellationToken);
}
