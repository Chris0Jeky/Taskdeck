using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class ThinkingStepService(
    ThinkingDeckService thinking, IThinkingDeckRepository decks, IUnitOfWork unit,
    CardService writer, IBoardRealtimeNotifier notifier)
{
    public async Task<Result<ThinkingDeckDto>> PromoteAsync(Guid actorId, Guid boardId, Guid cardId,
        Guid layerId, Guid itemId, PromoteThinkingStepDto dto, CancellationToken ct)
    {
        var access = await thinking.GetAsync(actorId, boardId, cardId, ct);
        if (!access.IsSuccess) return Result.Failure<ThinkingDeckDto>(access.ErrorCode, access.ErrorMessage);
        if (!access.Value.CanWrite)
            return Result.Failure<ThinkingDeckDto>(ErrorCodes.Forbidden, "This board is read-only.");
        var deck = await decks.GetAsync(cardId, ct);
        var layers = deck?.ReadLayers();
        var layer = layers?.SingleOrDefault(x => x.Id == layerId && x.Kind == "steps");
        var item = layer?.Items.SingleOrDefault(x => x.Id == itemId);
        if (deck is null || layer is null || item is null)
            return Result.Failure<ThinkingDeckDto>(ErrorCodes.NotFound, "Save this thinking step before creating a card.");
        // A lost response or repeated click returns the existing link, even with the old revision.
        // A deleted linked card remains a tombstone until the user explicitly removes the step.
        if (item.LinkedCardId.HasValue)
            return Result.Success(new ThinkingDeckDto(cardId, deck.Revision, deck.SchemaVersion, layers!, true));
        if (deck.Revision != dto.ExpectedRevision)
            return Result.Failure<ThinkingDeckDto>(ErrorCodes.Conflict, "Thinking changed. Reload before creating this card.");
        var childId = Guid.NewGuid();
        try
        {
            deck.Replace(layers!.Select(x => x.Id == layerId ? x with
            {
                Items = x.Items.Select(step => step.Id == itemId ? step with { LinkedCardId = childId, Completed = false } : step).ToArray()
            } : x).ToArray());
        }
        catch (DomainException ex) { return Result.Failure<ThinkingDeckDto>(ex.ErrorCode, ex.Message); }
        var staged = await writer.StageCardCreationAsync(new CreateCardDto(boardId, dto.ColumnId,
            dto.Title, item.Text, null, null), childId, ct);
        if (!staged.IsSuccess) return Result.Failure<ThinkingDeckDto>(staged.ErrorCode, staged.ErrorMessage);
        await unit.AuditLogs.AddAsync(new AuditLog("card", childId, AuditAction.Created, actorId,
            $"Created from thinking step; sourceCard={cardId}; layer={layerId}; step={itemId}"), ct);
        // One SaveChanges commits the child, board concurrency token, audit and deck CAS.
        // A WIP/archive/deck race rolls back every staged write and emits no notification.
        if (!await decks.SaveAsync(deck, dto.ExpectedRevision, ct))
            return Result.Failure<ThinkingDeckDto>(ErrorCodes.Conflict, "The board or thinking changed. Reload before retrying.");
        await notifier.NotifyBoardMutationAsync(new BoardRealtimeEvent(boardId, "card", "created", childId, DateTimeOffset.UtcNow), ct);
        return Result.Success(new ThinkingDeckDto(cardId, deck.Revision, deck.SchemaVersion, deck.ReadLayers(), true));
    }
}
