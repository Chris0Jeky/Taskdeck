using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Production-facing proposal service decorator that closes the automatic-expiry archive race.
///
/// The inner service owns proposal lifecycle behaviour, notifications and operator reporting. This
/// decorator owns only the missing second-stage board guard: it snapshots the current expiry
/// candidates, rejects a board that became archived after repository selection, and marks every
/// still-active board so an archive that commits later collides with the inner service's atomic save.
/// </summary>
public sealed class ProposalExpiryGuardedService(
    AutomationProposalService inner,
    IUnitOfWork unitOfWork,
    IAutomationPolicyEngine policyEngine) : IAutomationProposalService
{
    public Task<Result<ProposalDto>> CreateProposalAsync(
        CreateProposalDto dto,
        CancellationToken cancellationToken = default) =>
        inner.CreateProposalAsync(dto, cancellationToken);

    public Task<Result<ProposalDto>> CreateTranscriptProposalAsync(
        CreateProposalDto dto,
        IReadOnlyList<TranscriptEvidenceLinkInput> evidence,
        CancellationToken cancellationToken = default) =>
        inner.CreateTranscriptProposalAsync(dto, evidence, cancellationToken);

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
            // The repository-level archived-board filter is the first line of defence. Re-read the
            // candidate partition immediately before mutation and arm each extant active board's
            // concurrency marker. The inner service then reuses this same scoped unit of work:
            // - archive before this guard => InvalidOperation and no mutation;
            // - archive between this guard and the inner query => filtered out by that query;
            // - archive after the guard/query => board-marker concurrency conflict rolls back the
            //   proposal transition and its notification in the same save.
            var sweep = await unitOfWork.AutomationProposals.GetExpiredAsync(cancellationToken);
            if (sweep.Expirable.Count > 0)
            {
                var guard = await policyEngine.GuardProposalDecisionWritesAsync(
                    sweep.Expirable.Select(proposal => proposal.BoardId),
                    cancellationToken);
                if (!guard.IsSuccess)
                    return Result.Failure<int>(guard.ErrorCode, guard.ErrorMessage);
            }

            return await inner.ExpireProposalsAsync(cancellationToken);
        }
        catch (DomainException exception)
        {
            return Result.Failure<int>(exception.ErrorCode, exception.Message);
        }
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
