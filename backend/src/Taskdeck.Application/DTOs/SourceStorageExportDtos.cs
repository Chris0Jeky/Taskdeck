using Taskdeck.Application.Interfaces;

namespace Taskdeck.Application.DTOs;

public sealed record SourceStorageExportDto(
    IReadOnlyList<SourceBlobObjectExportDto> Objects,
    IReadOnlyList<SourceBlobReferenceExportDto> References,
    IReadOnlyList<SourceBlobChunkExportDto> Chunks,
    IReadOnlyList<RepresentationDescriptor> Representations,
    IReadOnlyList<ThinkingAudioExportDto> AudioAnswers);
public sealed record SourceBlobObjectExportDto(Guid Id, string ContentHash, long ByteSize);
public sealed record SourceBlobReferenceExportDto(Guid Id, Guid BlobId, string Modality, string? ReferrerKind, Guid? ReferrerId, DateTimeOffset AcquiredAt);
/// <summary>Concatenate decoded chunks by ordinal, then verify the object's byte size and SHA-256.</summary>
public sealed record SourceBlobChunkExportDto(Guid BlobId, int Ordinal, byte[] Content);
public sealed record ThinkingAudioExportDto(Guid Id, Guid? BoardId, Guid CardId, Guid LayerId, string QuestionHash,
    Guid CaptureId, Guid SourceAssetId, Guid UploadId, long Revision, Guid? RepresentationId, Guid? ConfirmedMemoryId,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
