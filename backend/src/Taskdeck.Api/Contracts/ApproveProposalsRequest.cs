using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Api.Contracts;

/// <summary>
/// Explicit proposal selection for the all-or-none batch-approve endpoint.
/// </summary>
public sealed class ApproveProposalsRequest
{
    [Required]
    public List<Guid>? Ids { get; init; }
}
