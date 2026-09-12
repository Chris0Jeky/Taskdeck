using Taskdeck.Domain.Entities;
namespace Taskdeck.Application.Interfaces;

public interface IBoardDependencyRepository
{
    Task<BoardDependencies?> GetAsync(Guid boardId, CancellationToken cancellationToken);
    Task<bool> SaveAsync(BoardDependencies graph, long expectedRevision, CancellationToken cancellationToken);
    Task<bool> StageAsync(BoardDependencies graph, long expectedRevision, CancellationToken cancellationToken);
    void AddForImport(BoardDependencies graph);
}
