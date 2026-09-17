using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class BoardEstimateRollupService(
    IUnitOfWork unitOfWork,
    ICardAssignmentStore assignments,
    IAuthorizationService authorization) : IBoardEstimateRollupService
{
    public async Task<Result<BoardEstimateRollupDto>> GetAsync(Guid boardId, Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (actingUserId == Guid.Empty)
            return Result.Failure<BoardEstimateRollupDto>(ErrorCodes.Forbidden, "You do not have access to this board.");

        // Assignments describe responsibility, never the requesting actor's authority.
        var permission = await authorization.CanReadBoardAsync(actingUserId, boardId);
        if (!permission.IsSuccess)
            return Result.Failure<BoardEstimateRollupDto>(permission.ErrorCode, permission.ErrorMessage);
        if (!permission.Value)
            return Result.Failure<BoardEstimateRollupDto>(ErrorCodes.Forbidden, "You do not have access to this board.");

        var cards = (await unitOfWork.Cards.GetForEstimateRollupsAsync(boardId, cancellationToken))
            .Where(card => card.BoardId == boardId && !card.IsArchived).ToArray();
        var columns = (await unitOfWork.Columns.GetByBoardIdAsync(boardId, cancellationToken))
            .Where(column => column.BoardId == boardId).OrderBy(column => column.Position).ThenBy(column => column.Id).ToArray();
        // Existing active owner/member query; owners need no BoardAccess row.
        var participants = await assignments.ReadParticipantsAsync(boardId, cancellationToken);
        var eligibleIds = participants.Select(user => user.Id).ToHashSet();
        var cardsByColumn = cards.ToLookup(card => card.ColumnId);
        var participantCards = cards.SelectMany(card => card.Assignments
                .Where(assignment => eligibleIds.Contains(assignment.UserId))
                .Select(assignment => assignment.UserId).Distinct()
                .Select(userId => (UserId: userId, Card: card)))
            .ToLookup(entry => entry.UserId, entry => entry.Card);

        return Result.Success(new BoardEstimateRollupDto(boardId, DateTimeOffset.UtcNow,
            Total(cards),
            columns.Select(column => new ColumnEstimateRollupDto(column.Id, column.Name, Total(cardsByColumn[column.Id]))).ToArray(),
            participants.OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase).ThenBy(user => user.Id)
                .Select(user => new ParticipantEstimateRollupDto(user.Id, user.Username, Total(participantCards[user.Id]))).ToArray(),
            Total(cards.Where(card => !card.Assignments.Any(assignment => eligibleIds.Contains(assignment.UserId))))));
    }

    private static EstimateTotalsDto Total(IEnumerable<Card> cards)
    {
        var count = 0;
        var missing = 0;
        long minutes = 0;
        foreach (var card in cards)
        {
            count++;
            if (card.EstimatedEffortMinutes is { } estimate) minutes += estimate;
            else missing++;
        }
        return new EstimateTotalsDto(count, minutes, missing);
    }
}
