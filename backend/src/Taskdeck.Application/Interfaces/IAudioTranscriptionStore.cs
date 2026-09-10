using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Interfaces;

/// <summary>Owns short admission/completion transactions; never holds a database transaction during speech transport.</summary>
public interface IAudioTranscriptionStore
{
    Task<Result<AudioTranscriptionStatusDto>> StatusAsync(Guid userId, Guid audioId, SpeechTranscriptionConfiguration configuration, DateTimeOffset now, CancellationToken ct);
    Task<Result<AudioTranscriptionAdmission>> AdmitAsync(Guid userId, Guid audioId, AudioTranscriptionRequestDto request,
        SpeechTranscriptionConfiguration configuration, DateTimeOffset now, CancellationToken ct);
    Task<Result<AudioTranscriptionReceiptDto>> FinishAsync(Guid userId, Guid attemptId, AudioTranscriptionProviderResult result, DateTimeOffset now, CancellationToken ct);
    Task<bool> DispatchAllowedAsync(Guid userId, Guid attemptId, DateTimeOffset now, CancellationToken ct);
    Task DeleteOwnerAsync(Guid userId, CancellationToken ct);
}
