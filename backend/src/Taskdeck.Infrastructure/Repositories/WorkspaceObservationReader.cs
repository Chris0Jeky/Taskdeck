using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class WorkspaceObservationReader(TaskdeckDbContext db) : IWorkspaceObservationReader
{
    public async Task<ObservationSourceDto?> SourceAsync(Guid userId, Guid boardId, Guid cardId, CancellationToken ct)
    {
        // BoardAccess.CanRead permits every membership role. No tracked board/membership can
        // carry an earlier permission or archive state across the model await.
        var card = await db.Cards.AsNoTracking().Where(card => card.Id == cardId && card.BoardId == boardId &&
            db.Users.Any(user => user.Id == userId && user.IsActive) &&
            db.Boards.Any(board => board.Id == boardId && !board.IsArchived && (board.OwnerId == userId ||
                db.BoardAccesses.Any(access => access.BoardId == boardId && access.UserId == userId))))
            .SingleOrDefaultAsync(ct);
        return card == null ? null : WorkspaceObservationContract.Source(card);
    }
}
