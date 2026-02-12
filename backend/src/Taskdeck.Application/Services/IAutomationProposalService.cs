using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

public interface IAutomationProposalService
{
    /// <summary>
    /// Creates a new automation proposal with operations.
    /// </summary>
    Task<Result<ProposalDto>> CreateProposalAsync(CreateProposalDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a proposal by ID with all operations.
    /// </summary>
    Task<Result<ProposalDto>> GetProposalByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets proposals with optional filters.
    /// </summary>
    Task<Result<IEnumerable<ProposalDto>>> GetProposalsAsync(ProposalFilterDto? filter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a pending proposal.
    /// </summary>
    Task<Result<ProposalDto>> ApproveProposalAsync(Guid id, Guid decidedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a pending proposal with optional reason (required for High/Critical risk).
    /// </summary>
    Task<Result<ProposalDto>> RejectProposalAsync(Guid id, Guid decidedByUserId, UpdateProposalStatusDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an approved proposal as applied.
    /// </summary>
    Task<Result<ProposalDto>> MarkAsAppliedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an approved proposal as failed with reason.
    /// </summary>
    Task<Result<ProposalDto>> MarkAsFailedAsync(Guid id, string failureReason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Expires all stale pending proposals.
    /// </summary>
    Task<Result<int>> ExpireProposalsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the diff preview for a proposal.
    /// </summary>
    Task<Result<string>> GetProposalDiffAsync(Guid id, CancellationToken cancellationToken = default);
}
