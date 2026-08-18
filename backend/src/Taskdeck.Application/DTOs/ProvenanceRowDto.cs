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
public record ProvenanceEvidenceLinkDto(
    string SourceType,
    string SourceId,
    string? Label,
    int? SpanStart,
    int? SpanEnd);
