using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
namespace Taskdeck.Application.Services;

public sealed class BoardRelationService(IBoardDependencyRepository graphs, IUnitOfWork unit,
    IAuthorizationService authorization) : IBoardRelationService
{
    public async Task<Result<BoardRelationsDto>> GetAsync(Guid actorId, Guid boardId, CancellationToken cancellationToken)
    {
        var access = await CheckAsync(actorId, boardId, false, cancellationToken);
        if (!access.IsSuccess) return Failure(access.ErrorCode, access.ErrorMessage);
        var graph = await graphs.GetAsync(boardId, cancellationToken) ?? new BoardDependencies(boardId);
        var writable = await authorization.CanWriteBoardAsync(actorId, boardId);
        return Result.Success(new BoardRelationsDto(boardId, graph.Revision, graph.ReadRelations(),
            !access.Value.IsArchived && writable.IsSuccess && writable.Value));
    }

    public async Task<Result<BoardRelationsDto>> ValidateMutationAsync(Guid actorId, Guid boardId, CardRelationEdge relation,
        long expectedRevision, bool remove, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(actorId, boardId, relation, expectedRevision, remove, cancellationToken);
        return prepared.IsSuccess ? Result.Success(prepared.Value.Dto) : Failure(prepared.ErrorCode, prepared.ErrorMessage);
    }

    public async Task<Result<BoardRelationsDto>> StageMutationAsync(Guid actorId, Guid boardId, CardRelationEdge relation,
        long expectedRevision, bool remove, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(actorId, boardId, relation, expectedRevision, remove, cancellationToken);
        if (!prepared.IsSuccess) return Failure(prepared.ErrorCode, prepared.ErrorMessage);
        var value = prepared.Value;
        value.Graph.ReplaceRelations(value.Dto.Relations);
        if (!await graphs.StageAsync(value.Graph, expectedRevision, cancellationToken)) return Conflict();
        // No-op version checks acquire the same row guards as card lifecycle/deletion, without
        // changing timestamps or work material. Ordered preceding creates remain tracked and valid.
        await unit.Cards.StageRelationEndpointGuardAsync(relation.SourceCardId, cancellationToken);
        await unit.Cards.StageRelationEndpointGuardAsync(relation.TargetCardId, cancellationToken);
        value.Board.RecordDependentMutation();
        return Result.Success(value.Dto);
    }

    private async Task<Result<Prepared>> PrepareAsync(Guid actorId, Guid boardId, CardRelationEdge relation,
        long expectedRevision, bool remove, CancellationToken ct)
    {
        var access = await CheckAsync(actorId, boardId, true, ct);
        if (!access.IsSuccess) return Result.Failure<Prepared>(access.ErrorCode, access.ErrorMessage);
        var graph = await graphs.GetAsync(boardId, ct) ?? new BoardDependencies(boardId);
        if (expectedRevision != graph.Revision) return Result.Failure<Prepared>(ErrorCodes.Conflict, "Relations changed. Reload before trying again.");
        try
        {
            var normalized = CardRelationRules.Normalize(relation);
            var cards = (await unit.Cards.GetHierarchyByBoardIdAsync(boardId, ct)).ToDictionary(c => c.Id);
            // Find includes Added cards from preceding proposal operations before SaveChanges.
            foreach (var id in new[] { normalized.SourceCardId, normalized.TargetCardId })
            {
                var card = await unit.Cards.GetByIdAsync(id, ct);
                if (card is not null) cards[id] = card;
            }
            var endpoints = cards.Values.Select(c => new CardRelationEndpoint(c.Id, c.BoardId, c.IsArchived)).ToArray();
            var candidate = CardRelationRules.Apply(boardId, graph.ReadRelations(), normalized, remove, endpoints);
            return Result.Success(new Prepared(access.Value, graph, new BoardRelationsDto(boardId, graph.Revision + 1, candidate, true)));
        }
        catch (DomainException ex) { return Result.Failure<Prepared>(ex.ErrorCode, ex.Message); }
    }

    private async Task<Result<Board>> CheckAsync(Guid actorId, Guid boardId, bool write, CancellationToken ct)
    {
        var permission = write ? await authorization.CanWriteBoardAsync(actorId, boardId) : await authorization.CanReadBoardAsync(actorId, boardId);
        if (!permission.IsSuccess) return Result.Failure<Board>(permission.ErrorCode, permission.ErrorMessage);
        if (!permission.Value) return Result.Failure<Board>(ErrorCodes.Forbidden, "You do not have access to this board.");
        var board = await unit.Boards.GetByIdAsync(boardId, ct);
        if (board is null) return Result.Failure<Board>(ErrorCodes.NotFound, "Board not found.");
        if (write && board.IsArchived) return Result.Failure<Board>(ErrorCodes.InvalidOperation, "Restore this board before editing relations.");
        return Result.Success(board);
    }
    private sealed record Prepared(Board Board, BoardDependencies Graph, BoardRelationsDto Dto);
    private static Result<BoardRelationsDto> Failure(string code, string message) => Result.Failure<BoardRelationsDto>(code, message);
    private static Result<BoardRelationsDto> Conflict() => Failure(ErrorCodes.Conflict, "Relations changed. Reload before trying again.");
}
