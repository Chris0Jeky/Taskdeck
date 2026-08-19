namespace Taskdeck.Application.DTOs;

/// <summary>
/// Represents a single provenance row for the Paper deep-Review surface.
/// Each row describes a source that was read, excluded, or inferred during
/// proposal generation.
/// </summary>
public record ProvenanceRowDto(
    string Icon,
    string Key,
    string Value,
    string Weight,
    IReadOnlyList<ProvenanceEvidenceLinkDto>? EvidenceLinks = null
);

/// <summary>Opaque evidence-link metadata; transcript text is never returned.</summary>
/// <param name="Viewable">
/// True only when the authenticated caller can actually open this evidence's source through
/// its read endpoint — for transcript evidence, when the caller owns that transcript.
/// <para>
/// Server-computed from the caller's claims; never accepted from the client. Provenance is
/// board-authorized while <c>GET /api/transcripts/{id}</c> stays owner-only, so a board
/// collaborator sees the evidence metadata but must not be offered a "view in transcript"
/// affordance that can only 404. The flag says nothing the caller does not already know
/// about their own access, and it does not change the 404 parity of a direct probe.
/// </para>
/// </param>
public record ProvenanceEvidenceLinkDto(
    string SourceType,
    string SourceId,
    string? Label,
    int? SpanStart,
    int? SpanEnd,
    bool Viewable);
