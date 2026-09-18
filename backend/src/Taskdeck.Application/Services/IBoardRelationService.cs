using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
namespace Taskdeck.Application.Services;
public interface IBoardRelationService
{
    Task<Result<BoardRelationsDto>> GetAsync(Guid actorId, Guid boardId, CancellationToken cancellationToken);
    Task<Result<BoardRelationsDto>> ValidateMutationAsync(Guid actorId, Guid boardId, CardRelationEdge relation,
        long expectedRevision, bool remove, CancellationToken cancellationToken);
    // Stages only. The proposal executor owns persistence, transaction, audit and notification.
    Task<Result<BoardRelationsDto>> StageMutationAsync(Guid actorId, Guid boardId, CardRelationEdge relation,
        long expectedRevision, bool remove, CancellationToken cancellationToken);
}
