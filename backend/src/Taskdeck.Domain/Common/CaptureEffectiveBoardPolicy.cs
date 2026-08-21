using Taskdeck.Domain.Entities;

namespace Taskdeck.Domain.Common;

/// <summary>
/// Resolves the effective board attached to a persisted capture without mutating retained history.
/// </summary>
public static class CaptureEffectiveBoardPolicy
{
    public static Guid? ResolveEffectiveBoardId(
        Guid captureId,
        Guid captureUserId,
        Guid? requestBoardId,
        Guid? provenanceBoardId,
        Guid? provenanceProposalId,
        DateTimeOffset? convertedAt,
        AutomationProposal? appliedProposal = null)
    {
        // The raw FK wins over server-stamped provenance when both exist. A client cannot supply
        // provenance because the capture write contract rejects server-attribution fields.
        var storedEffectiveBoardId = requestBoardId ?? provenanceBoardId;
        if (!IsValidatedAppliedProposal(
                captureId,
                captureUserId,
                provenanceProposalId,
                convertedAt,
                appliedProposal))
        {
            return storedEffectiveBoardId;
        }

        return storedEffectiveBoardId ?? appliedProposal!.BoardId;
    }

    public static bool IsValidatedAppliedProposal(
        Guid captureId,
        Guid captureUserId,
        Guid? provenanceProposalId,
        DateTimeOffset? convertedAt,
        AutomationProposal? appliedProposal)
    {
        if (!provenanceProposalId.HasValue ||
            provenanceProposalId.Value == Guid.Empty ||
            convertedAt is not null)
        {
            return false;
        }

        // Legacy captures may have a proposal link without a persisted board/converted timestamp.
        // Trust that fallback only when the applied Queue proposal is the same user's exact capture.
        if (appliedProposal == null ||
            appliedProposal.Status != ProposalStatus.Applied ||
            appliedProposal.SourceType != ProposalSourceType.Queue ||
            !string.Equals(
                appliedProposal.SourceReferenceId,
                captureId.ToString(),
                StringComparison.OrdinalIgnoreCase) ||
            appliedProposal.RequestedByUserId != captureUserId)
        {
            return false;
        }

        return true;
    }
}
