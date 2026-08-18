using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class ProposalFeedbackService : IProposalFeedbackService
{
    private readonly IUnitOfWork _unitOfWork;

    public ProposalFeedbackService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> ReportBadSuggestionAsync(
        Guid proposalId,
        Guid reportedByUserId,
        ProposalFeedbackReason reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(proposalId, cancellationToken);
            if (proposal == null)
                return Result.Failure(ErrorCodes.NotFound, $"Proposal with ID {proposalId} not found");

            var existing = await _unitOfWork.ProposalFeedbacks
                .GetByProposalAndUserAsync(proposalId, reportedByUserId, cancellationToken);

            if (existing is not null)
            {
                // One signal per user per proposal. A repeat is a no-op, except the first specific
                // reason refines an earlier one-click Unspecified (first-specific-wins).
                if (existing.Reason == ProposalFeedbackReason.Unspecified && reason != ProposalFeedbackReason.Unspecified)
                {
                    existing.UpdateReason(reason);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                return Result.Success();
            }

            var feedback = new ProposalFeedback(proposalId, reportedByUserId, reason);
            await _unitOfWork.ProposalFeedbacks.AddAsync(feedback, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DomainException ex)
        {
            // A racing concurrent first-report collides on the UNIQUE (ProposalId, ReportedByUserId)
            // index; UnitOfWork maps that to DomainException(Conflict). The row is identical by
            // construction, so treat the race as success -- the signal is already recorded.
            //
            // The same Conflict path also covers a concurrent reason REFINEMENT (two requests from
            // the one user upgrading the same Unspecified row to two different specific reasons at
            // once): the first commit wins on the UpdatedAt concurrency token and the second is a
            // benign no-op here. So the precise contract is "first-specific-wins for SEQUENTIAL
            // re-reports; first-committed-wins under simultaneous distinct reasons" -- a negligible
            // edge for a single-user signal, and the row's integrity is preserved either way
            // (see ADR-0043).
            if (ex.ErrorCode == ErrorCodes.Conflict)
                return Result.Success();

            return Result.Failure(ex.ErrorCode, ex.Message);
        }
    }
}
