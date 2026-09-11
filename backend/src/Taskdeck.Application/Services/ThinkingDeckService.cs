using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class ThinkingDeckService(ICardRepository cards, IThinkingDeckRepository decks, IAuthorizationService authorization, IBoardRepository boards)
{
    public async Task<Result<ThinkingDeckDto>> GetAsync(Guid actorId, Guid boardId, Guid cardId, CancellationToken ct)
    {
        var access = await CheckAsync(actorId, boardId, cardId, false, ct);
        if (!access.IsSuccess) return Result.Failure<ThinkingDeckDto>(access.ErrorCode, access.ErrorMessage);
        var writable = await authorization.CanWriteBoardAsync(actorId, boardId);
        var card = await cards.GetByIdAsync(cardId, ct);
        return Result.Success(Map(await decks.GetAsync(cardId, ct) ?? new ThinkingDeck(cardId), card is { IsArchived: false } && !access.Value.IsArchived && writable.IsSuccess && writable.Value));
    }

    public async Task<Result<ThinkingDeckDto>> SaveAsync(Guid actorId, Guid boardId, Guid cardId, SaveThinkingDeckDto dto, CancellationToken ct)
    {
        var access = await CheckAsync(actorId, boardId, cardId, true, ct);
        if (!access.IsSuccess) return Result.Failure<ThinkingDeckDto>(access.ErrorCode, access.ErrorMessage);
        var deck = await decks.GetAsync(cardId, ct) ?? new ThinkingDeck(cardId);
        if (dto.ExpectedRevision != deck.Revision)
            return Conflict();
        // Only promotion may introduce a card link. Removing a thought never removes its card.
        var saved = deck.ReadLayers();
        if (dto.Layers is not null && dto.Layers.Where(layer => layer?.Items is not null).Any(layer =>
            layer.Items.Any(item => item?.LinkedCardId is not null &&
                saved.FirstOrDefault(old => old.Id == layer.Id)?.Items.FirstOrDefault(old => old.Id == item.Id)?.LinkedCardId != item.LinkedCardId)))
            return Result.Failure<ThinkingDeckDto>(ErrorCodes.ValidationError, "Create linked cards through the saved step action.");
        try { deck.Replace(dto.Layers!); }
        catch (DomainException ex) { return Result.Failure<ThinkingDeckDto>(ex.ErrorCode, ex.Message); }
        // Join the board's concurrency-token guard to the same save as the deck.
        // An archive committed after CheckAsync must reject both initial inserts and updates.
        access.Value.RecordDependentMutation();
        if (!await decks.SaveAsync(deck, dto.ExpectedRevision, ct)) return Conflict();
        return Result.Success(Map(deck, true));
    }

    private async Task<Result<Board>> CheckAsync(Guid actorId, Guid boardId, Guid cardId, bool write, CancellationToken ct)
    {
        var permission = write ? await authorization.CanWriteBoardAsync(actorId, boardId) : await authorization.CanReadBoardAsync(actorId, boardId);
        if (!permission.IsSuccess) return Result.Failure<Board>(permission.ErrorCode, permission.ErrorMessage);
        if (!permission.Value) return Result.Failure<Board>(ErrorCodes.Forbidden, "You do not have access to this board.");
        var card = await cards.GetByIdAsync(cardId, ct);
        if (card?.BoardId != boardId) return Result.Failure<Board>(ErrorCodes.NotFound, "Card not found on this board.");
        if (write && card.IsArchived)
            return Result.Failure<Board>(ErrorCodes.InvalidOperation, "Restore the archived card before editing its thinking.");
        var board = await boards.GetByIdAsync(boardId, ct);
        if (board is null) return Result.Failure<Board>(ErrorCodes.NotFound, "Board not found.");
        if (write && board.IsArchived)
            return Result.Failure<Board>(ErrorCodes.InvalidOperation, "Cannot modify thinking on an archived board. Restore the board before editing.");
        return Result.Success(board);
    }
    private static ThinkingDeckDto Map(ThinkingDeck deck, bool canWrite) => new(deck.CardId, deck.Revision, deck.SchemaVersion, deck.ReadLayers(), canWrite);
    private static Result<ThinkingDeckDto> Conflict() => Result.Failure<ThinkingDeckDto>(ErrorCodes.Conflict, "Thinking deck changed. Reload the saved version before saving again.");
}
