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
    string Weight
);
