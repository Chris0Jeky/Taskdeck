using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services.Pipeline;

public static class ProposalAssignmentContract
{
    public const string Action = "replace-assignments";

    public static ReplaceCardAssignmentsDto Read(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("userIds", out var ids) || ids.ValueKind != JsonValueKind.Array ||
            ids.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String || !v.TryGetGuid(out var id) || id == Guid.Empty) ||
            !parameters.TryGetProperty("expectedUpdatedAt", out var stamp) || stamp.ValueKind != JsonValueKind.String ||
            !stamp.TryGetDateTimeOffset(out var expected))
            throw new DomainException(ErrorCodes.ValidationError, "Assignment replacement requires userIds and expectedUpdatedAt.");
        return new ReplaceCardAssignmentsDto(ids.EnumerateArray().Select(v => v.GetGuid()).Distinct().ToArray(), expected);
    }

    public static async Task<Result<string>> ValidateAsync(IUnitOfWork uow, Guid? boardId, JsonElement parameters, CancellationToken ct)
    {
        try
        {
            var dto = Read(parameters);
            if (!boardId.HasValue || !OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out var cardId, out _))
                return Result.Failure<string>(ErrorCodes.ValidationError, "Assignment replacement requires a board-scoped card.");
            var card = await uow.Cards.GetByIdWithLabelsAsync(cardId, ct);
            if (card is null || card.BoardId != boardId)
                return Result.Failure<string>(ErrorCodes.NotFound, "Card not found in this board.");
            var board = await uow.Boards.GetByIdAsync(boardId.Value, ct);
            if (board is null || board.IsArchived || card.IsArchived)
                return Result.Failure<string>(ErrorCodes.InvalidOperation, "Restore the board and card before changing assignments.");
            if (dto.ExpectedUpdatedAt != card.UpdatedAt)
                return Result.Failure<string>(ErrorCodes.Conflict, "Card changed. Refresh the assignment proposal.");
            var names = new List<string>();
            foreach (var id in dto.UserIds!)
            {
                var user = await uow.Users.GetByIdAsync(id, ct);
                if (user is not { IsActive: true } || (board.OwnerId != id &&
                    await uow.BoardAccesses.GetByBoardAndUserAsync(board.Id, id, ct) is null))
                    return Result.Failure<string>(ErrorCodes.ValidationError, "Every assignee must be an active participant of this board.");
                names.Add(user.Username);
            }
            var before = card.Assignments.Select(a => a.User?.Username ?? "Participant").Order().ToArray();
            return Result.Success($"Assignees on '{card.Title}': {(before.Length == 0 ? "Unassigned" : string.Join(", ", before))} -> {(names.Count == 0 ? "Unassigned" : string.Join(", ", names.Order()))}");
        }
        catch (DomainException ex) { return Result.Failure<string>(ex.ErrorCode, ex.Message); }
    }
}
