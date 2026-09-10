using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IWorkspaceInsightRepository
{
    Task<IReadOnlyList<WorkspaceMemory>> MemoriesByUserAsync(Guid userId, int limit, int offset, CancellationToken ct);
    Task<IReadOnlyList<QuietInsight>> InsightsByUserAsync(Guid userId, int limit, int offset, CancellationToken ct);
    Task<(int Memories, int Revisions, int Insights)> DeleteByUserAsync(Guid userId, CancellationToken ct);
    Task<List<QuietInsight>> InsightsAsync(Guid userId, Guid boardId, CancellationToken ct);
    Task<QuietInsight?> InsightAsync(Guid userId, Guid id, CancellationToken ct);
    Task<List<WorkspaceMemory>> MemoriesAsync(Guid userId, Guid boardId, CancellationToken ct);
    Task<WorkspaceMemory?> MemoryAsync(Guid userId, Guid id, CancellationToken ct);
    Task<WorkspaceMemory?> ThinkingAnswerAsync(Guid userId, Guid cardId, Guid layerId, string questionHash, CancellationToken ct);
    void Add(QuietInsight insight);
    void Add(WorkspaceMemory memory);
    void GuardMemoryRevision(WorkspaceMemory memory);
    Task<bool> SaveAsync(CancellationToken ct);
    /// <summary>Revalidates source/access and saves staged observations in one transaction.</summary>
    Task<bool> SaveObservationAsync(Guid userId, Guid boardId, Guid cardId, string fingerprint, CancellationToken ct);
}
