using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IThinkingAudioRepository
{
    Task<ThinkingAudioAnswer?> QuestionAsync(Guid userId, Guid cardId, Guid layerId, string hash, CancellationToken ct);
    Task<ThinkingAudioAnswer?> GetAsync(Guid userId, Guid id, CancellationToken ct);
    Task<ThinkingAudioAnswer?> UploadAsync(Guid userId, Guid uploadId, CancellationToken ct);
    void Add(ThinkingAudioAnswer answer);
    Task<bool> SaveAsync(CancellationToken ct);
}
