using Taskdeck.Domain.Entities;
using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Interfaces;

public interface IThinkingAudioRepository
{
    Task<IReadOnlyList<ThinkingAudioAnswer>> ListByUserAsync(Guid userId, int offset, int limit, CancellationToken ct);
    Task<IReadOnlyList<ThinkingAudioLibraryEntry>> LibraryEntriesAsync(Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<ThinkingAudioAnswer?> QuestionAsync(Guid userId, Guid cardId, Guid layerId, string hash, CancellationToken ct);
    Task<ThinkingAudioAnswer?> GetAsync(Guid userId, Guid id, CancellationToken ct);
    Task<ThinkingAudioAnswer?> UploadAsync(Guid userId, Guid uploadId, CancellationToken ct);
    void Add(ThinkingAudioAnswer answer);
    Task<bool> SaveAsync(CancellationToken ct);
}
