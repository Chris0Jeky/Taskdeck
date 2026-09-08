using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IWorkspaceInsightRepository
{
    Task<List<QuietInsight>> InsightsAsync(Guid userId, Guid boardId, CancellationToken ct);
    Task<QuietInsight?> InsightAsync(Guid userId, Guid id, CancellationToken ct);
    Task<List<WorkspaceMemory>> MemoriesAsync(Guid userId, Guid boardId, CancellationToken ct);
    Task<WorkspaceMemory?> MemoryAsync(Guid userId, Guid id, CancellationToken ct);
    Task<WorkspaceMemory?> ThinkingAnswerAsync(Guid userId, Guid cardId, Guid layerId, string questionHash, CancellationToken ct);
    void Add(QuietInsight insight);
    void Add(WorkspaceMemory memory);
    Task<bool> SaveAsync(CancellationToken ct);
}
