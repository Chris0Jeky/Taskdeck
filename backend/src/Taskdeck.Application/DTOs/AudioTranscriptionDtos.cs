using Taskdeck.Application.Services;

namespace Taskdeck.Application.DTOs;

public sealed record AudioTranscriptionRequestDto(Guid RequestId, long ExpectedRevision, string ConfigurationHash);
public sealed record AudioTranscriptionReceiptDto(Guid Id, Guid RequestId, Guid AudioAnswerId, string State,
    string Provider, string Model, string ConfigurationHash, DateTimeOffset StartedAt, DateTimeOffset Deadline,
    DateTimeOffset? FinishedAt, string? FailureCode, Guid? RepresentationId, string? Text);
public sealed record AudioTranscriptionStatusDto(SpeechTranscriptionConfiguration Configuration,
    int AttemptsUsedToday, long InputBytesUsedToday, IReadOnlyList<AudioTranscriptionReceiptDto> Attempts);
public sealed record AudioTranscriptionSource(Guid ReferenceId, long ByteSize, string ContentHash, string MediaType);
public sealed record AudioTranscriptionAdmission(AudioTranscriptionReceiptDto Receipt, AudioTranscriptionSource? Source);
public sealed record AudioTranscriptionAttemptExportDto(Guid Id, Guid UserId, Guid AudioAnswerId, Guid CaptureId,
    Guid SourceAssetId, Guid RequestId, string RequestHash, string ConfigurationHash, string Provider, string Model,
    DateTimeOffset StartedAt, DateTimeOffset Deadline, DateTimeOffset? FinishedAt, string State, string? FailureCode,
    Guid? RepresentationId, long Revision);
public sealed record AudioTranscriptionBudgetExportDto(Guid UserId, int UtcDay, int Attempts, long InputBytes, long Revision);
