using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class WorkspacePlanService(IWorkspacePlanRepository plans, IAuthorizationService authorization,
    IBoardRepository boards, ICardRepository cards, IColumnRepository columns)
{
    public async Task<Result<WorkspacePlanDto>> GetAsync(Guid actorId, CancellationToken ct) =>
        Result.Success(await MapAsync(actorId, await plans.GetAsync(actorId, ct), ct));

    public async Task<Result<WorkspacePlanDto>> SaveAsync(Guid actorId, SaveWorkspacePlanDto dto, CancellationToken ct)
    {
        var preference = await plans.GetAsync(actorId, ct);
        if (preference.PersonalPlanRevision != dto.ExpectedRevision) return Conflict();
        var previous = preference.ReadPersonalPlan();
        var next = new PersonalPlan(dto.Entries, previous.LastWorked);
        try { next.Validate(); }
        catch (DomainException ex) { return Result.Failure<WorkspacePlanDto>(ex.ErrorCode, ex.Message); }
        foreach (var entry in next.Entries)
        {
            var old = previous.Entries.FirstOrDefault(item => item.CardId == entry.CardId && item.BoardId == entry.BoardId);
            // An inaccessible retained reference may be removed, but cannot be introduced or rescheduled.
            if (old == entry) continue;
            if (!(await ReadCardAsync(actorId, entry.BoardId, entry.CardId, ct)).Available)
                return Unavailable();
        }
        preference.ReplacePersonalPlan(next);
        if (!await plans.SaveAsync(preference, dto.ExpectedRevision, ct)) return Conflict();
        return Result.Success(await MapAsync(actorId, preference, ct));
    }

    public async Task<Result<WorkspacePlanDto>> FocusAsync(Guid actorId, FocusWorkspacePlanDto dto, CancellationToken ct)
    {
        if (dto.BoardId == Guid.Empty || dto.CardId == Guid.Empty)
            return Result.Failure<WorkspacePlanDto>(ErrorCodes.ValidationError, "Focus requires a board and card.");
        var preference = await plans.GetAsync(actorId, ct);
        if (preference.PersonalPlanRevision != dto.ExpectedRevision) return Conflict();
        if (!(await ReadCardAsync(actorId, dto.BoardId, dto.CardId, ct)).Available) return Unavailable();
        var previous = preference.ReadPersonalPlan();
        preference.ReplacePersonalPlan(previous with { LastWorked = new(dto.BoardId, dto.CardId, DateTimeOffset.UtcNow) });
        if (!await plans.SaveAsync(preference, dto.ExpectedRevision, ct)) return Conflict();
        return Result.Success(await MapAsync(actorId, preference, ct));
    }

    private async Task<WorkspacePlanCardDto> ReadCardAsync(Guid actorId, Guid boardId, Guid cardId, CancellationToken ct)
    {
        var unavailable = new WorkspacePlanCardDto(boardId, cardId, false, null, null, null, null, false, null);
        var permission = await authorization.CanReadBoardAsync(actorId, boardId);
        if (!permission.IsSuccess || !permission.Value) return unavailable;
        var board = await boards.GetByIdAsync(boardId, ct);
        if (board is null || board.IsArchived) return unavailable;
        var card = await cards.GetByIdAsync(cardId, ct);
        if (card is null || card.BoardId != boardId) return unavailable;
        var column = await columns.GetByIdAsync(card.ColumnId, ct);
        return new(boardId, cardId, true, card.Title, board.Name, column?.Name, card.DueDate, card.IsBlocked, card.BlockReason);
    }

    private async Task<WorkspacePlanDto> MapAsync(Guid actorId, UserPreference preference, CancellationToken ct)
    {
        var material = preference.ReadPersonalPlan();
        var entries = new List<WorkspacePlanEntryDto>();
        foreach (var entry in material.Entries)
        {
            var card = await ReadCardAsync(actorId, entry.BoardId, entry.CardId, ct);
            entries.Add(new(card.BoardId, card.CardId, entry.PlannedDate, card.Available, card.Title, card.BoardName,
                card.ColumnName, card.DueDate, card.IsBlocked, card.BlockReason));
        }
        WorkspacePlanFocusDto? focus = null;
        if (material.LastWorked is { } last)
        {
            var card = await ReadCardAsync(actorId, last.BoardId, last.CardId, ct);
            focus = new(card.BoardId, card.CardId, card.Available, card.Title, card.BoardName, card.ColumnName,
                card.DueDate, card.IsBlocked, card.BlockReason, last.WorkedAt);
        }
        return new(preference.PersonalPlanRevision, entries, focus);
    }
    private static Result<WorkspacePlanDto> Conflict() => Result.Failure<WorkspacePlanDto>(ErrorCodes.Conflict, "Your personal plan changed. Refresh it before trying again.");
    private static Result<WorkspacePlanDto> Unavailable() => Result.Failure<WorkspacePlanDto>(ErrorCodes.Forbidden, "This card is not available for your personal plan.");
}
