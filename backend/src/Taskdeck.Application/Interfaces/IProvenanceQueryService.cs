using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Queries the provenance chain for a proposal and maps it to the
/// Paper deep-Review DTO shape (icon, key, value, weight).
/// </summary>
public interface IProvenanceQueryService
{
    /// <summary>
    /// Returns the provenance rows for the specified proposal.
    /// The caller's userId is validated for read access to the proposal's board.
    /// Returns an empty list (not an error) when the proposal has no provenance.
    /// </summary>
    Task<Result<IReadOnlyList<ProvenanceRowDto>>> GetProvenanceRowsAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default);
}
