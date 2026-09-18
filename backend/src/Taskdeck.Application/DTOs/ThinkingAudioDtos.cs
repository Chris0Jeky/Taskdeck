namespace Taskdeck.Application.DTOs;

public sealed record ThinkingAudioUploadDto(Guid UploadId, long ExpectedDeckRevision, long ByteSize, string FileName);
public sealed record ThinkingAudioWriteDto(long ExpectedRevision, string Text, Guid? SourceRepresentationId = null);
public sealed record ThinkingAudioConfirmDto(long ExpectedRevision, long ExpectedDeckRevision, Guid RepresentationId, string Status);
public sealed record ThinkingAudioRepresentationDto(Guid Id, string Text, string Quality, Guid? SupersededById);
public sealed record ThinkingAudioDto(Guid Id, long Revision, Guid CaptureId, Guid SourceAssetId, string QuestionHash,
    string FileName, string MediaType, long ByteSize, string ContentHash, string OriginalEvidence,
    Guid? RepresentationId, Guid? ConfirmedMemoryId, IReadOnlyList<ThinkingAudioRepresentationDto> WrittenVersions);
public sealed record ThinkingAudioDownload(Stream Content, string MediaType, string FileName);
public sealed record ThinkingAudioLibraryEntry(Guid Id, string FileName, long ByteSize, DateTimeOffset CreatedAt,
    string QuestionExcerpt, bool HasWrittenVersion, bool HasConfirmedAnswer, bool BoardRemoved);
public sealed record ThinkingAudioLibraryPage(IReadOnlyList<ThinkingAudioLibraryEntry> Items, int? NextOffset);
public sealed record ThinkingAudioLibraryDetail(ThinkingAudioDto Recording, Guid? CurrentBoardId, Guid? CurrentCardId);
