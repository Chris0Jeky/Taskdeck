using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class CardService
{
    private readonly IUnitOfWork _unitOfWork;

    public CardService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CardDto>> CreateCardAsync(CreateCardDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            // Verify board and column exist
            var board = await _unitOfWork.Boards.GetByIdAsync(dto.BoardId, cancellationToken);
            if (board == null)
                return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Board with ID {dto.BoardId} not found");

            var column = await _unitOfWork.Columns.GetByIdWithCardsAsync(dto.ColumnId, cancellationToken);
            if (column == null)
                return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Column with ID {dto.ColumnId} not found");

            // Check WIP limit
            if (column.WouldExceedWipLimitIfAdded())
                return Result.Failure<CardDto>(ErrorCodes.WipLimitExceeded,
                    $"Cannot add card, column '{column.Name}' has reached its WIP limit of {column.WipLimit}");

            // Determine position (add to bottom)
            var position = column.Cards.Any() ? column.Cards.Max(c => c.Position) + 1 : 0;

            var card = new Card(dto.BoardId, dto.ColumnId, dto.Title, dto.Description, dto.DueDate, position);
            await _unitOfWork.Cards.AddAsync(card, cancellationToken);

            // Add labels if provided
            if (dto.LabelIds != null && dto.LabelIds.Any())
            {
                var labels = await _unitOfWork.Labels.GetByBoardIdAsync(dto.BoardId, cancellationToken);
                var validLabelIds = labels.Select(l => l.Id).ToHashSet();

                foreach (var labelId in dto.LabelIds.Where(validLabelIds.Contains))
                {
                    var cardLabel = new CardLabel(card.Id, labelId);
                    card.AddLabel(cardLabel);
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var createdCard = await _unitOfWork.Cards.GetByIdWithLabelsAsync(card.Id, cancellationToken);
            return Result.Success(MapToDto(createdCard!));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CardDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<CardDto>> UpdateCardAsync(Guid id, UpdateCardDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var card = await _unitOfWork.Cards.GetByIdWithLabelsAsync(id, cancellationToken);
            if (card == null)
                return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Card with ID {id} not found");

            // Update basic fields
            if (dto.Title != null || dto.Description != null || dto.DueDate.HasValue)
                card.Update(dto.Title, dto.Description, dto.DueDate);

            // Update blocked status
            if (dto.IsBlocked.HasValue)
            {
                if (dto.IsBlocked.Value && !string.IsNullOrEmpty(dto.BlockReason))
                    card.Block(dto.BlockReason);
                else if (!dto.IsBlocked.Value)
                    card.Unblock();
            }

            // Update labels
            if (dto.LabelIds != null)
            {
                card.ClearLabels();
                var labels = await _unitOfWork.Labels.GetByBoardIdAsync(card.BoardId, cancellationToken);
                var validLabelIds = labels.Select(l => l.Id).ToHashSet();

                foreach (var labelId in dto.LabelIds.Where(validLabelIds.Contains))
                {
                    var cardLabel = new CardLabel(card.Id, labelId);
                    card.AddLabel(cardLabel);
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var updatedCard = await _unitOfWork.Cards.GetByIdWithLabelsAsync(id, cancellationToken);
            return Result.Success(MapToDto(updatedCard!));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CardDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<CardDto>> MoveCardAsync(Guid id, MoveCardDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var card = await _unitOfWork.Cards.GetByIdWithLabelsAsync(id, cancellationToken);
            if (card == null)
                return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Card with ID {id} not found");

            var targetColumn = await _unitOfWork.Columns.GetByIdWithCardsAsync(dto.TargetColumnId, cancellationToken);
            if (targetColumn == null)
                return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Column with ID {dto.TargetColumnId} not found");

            // Check WIP limit (only if moving to a different column)
            if (card.ColumnId != dto.TargetColumnId && targetColumn.WouldExceedWipLimitIfAdded())
                return Result.Failure<CardDto>(ErrorCodes.WipLimitExceeded,
                    $"Cannot move card, target column '{targetColumn.Name}' has reached its WIP limit of {targetColumn.WipLimit}");

            // Move card
            card.MoveToColumn(dto.TargetColumnId, dto.TargetPosition);

            // Reorder other cards in target column
            var cardsInTargetColumn = await _unitOfWork.Cards.GetByColumnIdAsync(dto.TargetColumnId, cancellationToken);
            var orderedCards = cardsInTargetColumn
                .Where(c => c.Id != card.Id)
                .OrderBy(c => c.Position)
                .ToList();

            orderedCards.Insert(dto.TargetPosition, card);

            for (int i = 0; i < orderedCards.Count; i++)
            {
                orderedCards[i].SetPosition(i);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var movedCard = await _unitOfWork.Cards.GetByIdWithLabelsAsync(id, cancellationToken);
            return Result.Success(MapToDto(movedCard!));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CardDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<IEnumerable<CardDto>>> SearchCardsAsync(
        Guid boardId,
        string? searchText = null,
        Guid? labelId = null,
        Guid? columnId = null,
        CancellationToken cancellationToken = default)
    {
        var cards = await _unitOfWork.Cards.SearchAsync(boardId, searchText, labelId, columnId, cancellationToken);
        return Result.Success(cards.Select(MapToDto));
    }

    public async Task<Result> DeleteCardAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var card = await _unitOfWork.Cards.GetByIdAsync(id, cancellationToken);
        if (card == null)
            return Result.Failure(ErrorCodes.NotFound, $"Card with ID {id} not found");

        await _unitOfWork.Cards.DeleteAsync(card, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static CardDto MapToDto(Card card)
    {
        var labels = card.CardLabels
            .Select(cl => new LabelDto(
                cl.Label.Id,
                cl.Label.BoardId,
                cl.Label.Name,
                cl.Label.ColorHex,
                cl.Label.CreatedAt,
                cl.Label.UpdatedAt
            ))
            .ToList();

        return new CardDto(
            card.Id,
            card.BoardId,
            card.ColumnId,
            card.Title,
            card.Description,
            card.DueDate,
            card.IsBlocked,
            card.BlockReason,
            card.Position,
            labels,
            card.CreatedAt,
            card.UpdatedAt
        );
    }
}
