namespace Taskdeck.Application.DTOs;

/// <summary>
/// Deterministic extractor output before it is persisted as reviewable history.
/// </summary>
public sealed record ArtefactExtractionResult(
    string ExtractedText,
    IReadOnlyList<string> Warnings,
    string ExtractorName,
    string ExtractorVersion);

public sealed record ArtefactExtractionDto(
    Guid Id,
    Guid SourceArtefactId,
    string ExtractorName,
    string ExtractorVersion,
    IReadOnlyList<string> Warnings,
    string ExtractedText,
    int TextLength,
    DateTimeOffset CreatedAt);

public sealed record UserDataExportArtefactExtractionDto(
    Guid Id,
    string ExtractorName,
    string ExtractorVersion,
    IReadOnlyList<string> Warnings,
    string ExtractedText,
    int TextLength,
    DateTimeOffset CreatedAt);
