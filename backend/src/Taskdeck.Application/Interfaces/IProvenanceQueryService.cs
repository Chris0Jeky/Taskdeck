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
    /// <param name="proposalId">The proposal whose provenance is requested.</param>
    /// <param name="callerUserId">
    /// The authenticated caller, resolved from claims by the controller. Used only to compute
    /// <see cref="ProvenanceEvidenceLinkDto.Viewable"/> per evidence link; it never widens or
    /// narrows which rows are returned.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<IReadOnlyList<ProvenanceRowDto>>> GetProvenanceRowsAsync(
        Guid proposalId,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the server-recorded producer metadata (provider/model/promptVersion) for the
    /// specified proposal. Board read authorization is enforced by the controller exactly as for
    /// <see cref="GetProvenanceRowsAsync"/>; this projection exposes no capture contents, so a
    /// board collaborator who cannot open the owner-only capture-detail endpoint can still see
    /// what produced the proposal they are reviewing (#2315 state 3).
    /// <para>
    /// A proposal with no recorded provenance succeeds with an all-null payload rather than
    /// failing: "nothing was recorded" is a legitimate answer and must stay distinguishable from
    /// an error, which the caller renders as no producer claim.
    /// </para>
    /// </summary>
    Task<Result<ProposalProvenanceMetadataDto>> GetProvenanceMetadataAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default);
}
