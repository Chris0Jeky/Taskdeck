using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.DTOs;

public sealed record SourceArtefactDto(
    Guid Id,
    Guid? BoardId,
    ArtefactKind Kind,
    string MimeType,
    string FileName,
    long ByteSize,
    string Sha256,
    CaptureSource CaptureSource,
    string? OriginReference,
    Guid? CreatedFromCaptureId,
    DateTimeOffset CreatedAt);

public sealed record UserDataExportArtefactDto(
    Guid Id,
    Guid? BoardId,
    string Kind,
    string MimeType,
    string FileName,
    long ByteSize,
    string Sha256,
    string CaptureSource,
    string? OriginReference,
    Guid? CreatedFromCaptureId,
    DateTimeOffset CreatedAt,
    string ContentBase64,
    IReadOnlyList<UserDataExportArtefactExtractionDto>? Extractions = null);
