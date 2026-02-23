using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Enums;

/// <summary>
/// Canonical capture lifecycle status used by capture-facing APIs/UX.
/// </summary>
public enum CaptureStatus
{
    New = 0,
    Triaging = 1,
    Triaged = 2,
    ProposalCreated = 3,
    Converted = 4,
    Ignored = 5,
    Failed = 6
}

public static class CaptureStatusPolicy
{
    private static readonly IReadOnlyDictionary<CaptureStatus, HashSet<CaptureStatus>> AllowedTransitions =
        new Dictionary<CaptureStatus, HashSet<CaptureStatus>>
        {
            [CaptureStatus.New] = new() { CaptureStatus.Triaging, CaptureStatus.Ignored },
            [CaptureStatus.Triaging] = new() { CaptureStatus.Triaged, CaptureStatus.ProposalCreated, CaptureStatus.Failed },
            [CaptureStatus.Triaged] = new() { CaptureStatus.Triaging, CaptureStatus.ProposalCreated, CaptureStatus.Ignored },
            [CaptureStatus.ProposalCreated] = new() { CaptureStatus.Converted, CaptureStatus.Triaging, CaptureStatus.Ignored },
            [CaptureStatus.Converted] = new(),
            [CaptureStatus.Ignored] = new(),
            [CaptureStatus.Failed] = new() { CaptureStatus.Triaging, CaptureStatus.Ignored }
        };

    /// <summary>
    /// Derives capture status from queue status while preserving proposal/apply linkage context.
    /// </summary>
    public static CaptureStatus MapFromQueueStatus(
        RequestStatus queueStatus,
        bool hasLinkedProposal = false,
        bool isConverted = false)
    {
        if (isConverted)
        {
            return CaptureStatus.Converted;
        }

        return queueStatus switch
        {
            RequestStatus.Pending => CaptureStatus.New,
            RequestStatus.Processing => CaptureStatus.Triaging,
            RequestStatus.Completed => hasLinkedProposal ? CaptureStatus.ProposalCreated : CaptureStatus.Triaged,
            RequestStatus.Cancelled => CaptureStatus.Ignored,
            RequestStatus.Failed => CaptureStatus.Failed,
            _ => throw new DomainException(ErrorCodes.ValidationError, $"Unknown queue status: {queueStatus}")
        };
    }

    public static bool CanTransition(CaptureStatus from, CaptureStatus to)
    {
        if (from == to)
        {
            return true;
        }

        if (!AllowedTransitions.TryGetValue(from, out var allowed))
        {
            return false;
        }

        return allowed.Contains(to);
    }
}
