using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Interfaces;

/// <summary>Fresh, untracked reads with active-board ownership/membership enforced in the query.</summary>
public interface IWorkspaceObservationReader
{
    Task<ObservationSourceDto?> SourceAsync(Guid userId, Guid boardId, Guid cardId, CancellationToken ct);
}
