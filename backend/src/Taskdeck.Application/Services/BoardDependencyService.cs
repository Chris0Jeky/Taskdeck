using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
namespace Taskdeck.Application.Services;

public sealed class BoardDependencyService(IBoardDependencyRepository graphs, IUnitOfWork unit,
    IAuthorizationService authorization, IBoardRealtimeNotifier notifier)
{
    public async Task<Result<BoardDependencyDto>> GetAsync(Guid actorId, Guid boardId, CancellationToken ct)
    {
        var access = await CheckAsync(actorId, boardId, false, ct);
        if (!access.IsSuccess) return Result.Failure<BoardDependencyDto>(access.ErrorCode, access.ErrorMessage);
        var graph = await graphs.GetAsync(boardId, ct) ?? new BoardDependencies(boardId);
        var ids = (await unit.Cards.GetByBoardIdAsync(boardId, ct)).Select(card => card.Id).ToHashSet();
        var writable = await authorization.CanWriteBoardAsync(actorId, boardId);
        return Result.Success(new BoardDependencyDto(boardId, graph.Revision,
            graph.ReadEdges().Where(e => ids.Contains(e.CardId) && ids.Contains(e.DependsOnCardId)).ToArray(),
            !access.Value.IsArchived && writable.IsSuccess && writable.Value));
    }

    public async Task<Result<BoardDependencyDto>> SaveAsync(Guid actorId, Guid boardId, SaveBoardDependenciesDto dto, CancellationToken ct)
    {
        var access = await CheckAsync(actorId, boardId, true, ct);
        if (!access.IsSuccess) return Result.Failure<BoardDependencyDto>(access.ErrorCode, access.ErrorMessage);
        var graph = await graphs.GetAsync(boardId, ct) ?? new BoardDependencies(boardId);
        if (dto.ExpectedRevision != graph.Revision) return Conflict();
        var ids = (await unit.Cards.GetByBoardIdAsync(boardId, ct)).Select(card => card.Id).ToHashSet();
        if (dto.Edges is null)
            return Result.Failure<BoardDependencyDto>(ErrorCodes.ValidationError, "Dependencies are required.");
        if (dto.Edges.Any(e => e is null || !ids.Contains(e.CardId) || !ids.Contains(e.DependsOnCardId)))
            return Result.Failure<BoardDependencyDto>(ErrorCodes.ValidationError, "Every dependency must reference an existing card on this board.");
        var archivedIds = (await unit.Cards.GetArchivedByBoardIdAsync(boardId, ct)).Select(card => card.Id).ToHashSet();
        var existingIds = ids.Concat(archivedIds).ToHashSet();
        // The editor submits only active edges. Retain hidden archive history until its
        // endpoints are restored; deleted endpoints remain removable as before.
        var retained = graph.ReadEdges().Where(edge =>
            existingIds.Contains(edge.CardId) && existingIds.Contains(edge.DependsOnCardId) &&
            (archivedIds.Contains(edge.CardId) || archivedIds.Contains(edge.DependsOnCardId)));
        try { graph.Replace([.. dto.Edges, .. retained]); }
        catch (DomainException ex) { return Result.Failure<BoardDependencyDto>(ex.ErrorCode, ex.Message); }
        access.Value.RecordDependentMutation();
        await unit.AuditLogs.AddAsync(new AuditLog("board", boardId, AuditAction.Updated, actorId,
            $"Updated explicit card dependencies; edges={dto.Edges.Count}"), ct);
        // The board token guards archive; the repository revalidates card existence
        // inside the write transaction so deletion cannot introduce a dangling link.
        if (!await graphs.SaveAsync(graph, dto.ExpectedRevision, ct)) return Conflict();
        await notifier.NotifyBoardMutationAsync(new BoardRealtimeEvent(boardId, "board", "updated", boardId, DateTimeOffset.UtcNow), ct);
        return Result.Success(new BoardDependencyDto(boardId, graph.Revision,
            graph.ReadEdges().Where(edge => ids.Contains(edge.CardId) && ids.Contains(edge.DependsOnCardId)).ToArray(), true));
    }

    private async Task<Result<Board>> CheckAsync(Guid actorId, Guid boardId, bool write, CancellationToken ct)
    {
        var permission = write ? await authorization.CanWriteBoardAsync(actorId, boardId) : await authorization.CanReadBoardAsync(actorId, boardId);
        if (!permission.IsSuccess) return Result.Failure<Board>(permission.ErrorCode, permission.ErrorMessage);
        if (!permission.Value) return Result.Failure<Board>(ErrorCodes.Forbidden, "You do not have access to this board.");
        var board = await unit.Boards.GetByIdAsync(boardId, ct);
        if (board is null) return Result.Failure<Board>(ErrorCodes.NotFound, "Board not found.");
        if (write && board.IsArchived) return Result.Failure<Board>(ErrorCodes.InvalidOperation, "Restore this board before editing dependencies.");
        return Result.Success(board);
    }
    private static Result<BoardDependencyDto> Conflict() => Result.Failure<BoardDependencyDto>(ErrorCodes.Conflict,
        "The board or dependencies changed. Reload before saving again.");
}
