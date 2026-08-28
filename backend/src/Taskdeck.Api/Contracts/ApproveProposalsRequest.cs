using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Api.Contracts;

/// <summary>
/// Explicit proposal selection for the all-or-none batch-approve endpoint.
/// </summary>
public sealed class ApproveProposalsRequest
{
    [Required]
    public List<ApproveProposalSelectionRequest>? Proposals { get; init; }
}

/// <summary>
/// The exact pending-proposal snapshot the reviewer selected. Both concurrency values are
/// revalidated atomically before any proposal in the batch transitions.
/// </summary>
public sealed class ApproveProposalSelectionRequest
{
    public Guid Id { get; init; }

    public DateTimeOffset ExpectedProposalUpdatedAt { get; init; }

    public Guid? ExpectedLatestRevisionId { get; init; }
}
