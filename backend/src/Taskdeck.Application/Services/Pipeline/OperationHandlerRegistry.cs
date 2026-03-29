using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services.Pipeline;

/// <summary>
/// Dispatches automation proposal operations to the appropriate service handler
/// based on target type and action type.
/// </summary>
public class OperationHandlerRegistry
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly CardService _cardService;
    private readonly BoardService _boardService;
    private readonly ColumnService _columnService;

    public OperationHandlerRegistry(
        IUnitOfWork unitOfWork,
        CardService cardService,
        BoardService boardService,
        ColumnService columnService)
    {
        _unitOfWork = unitOfWork;
        _cardService = cardService;
        _boardService = boardService;
        _columnService = columnService;
    }

    public async Task<Result> ExecuteOperationAsync(ProposalOperationDto operation, CancellationToken cancellationToken)
    {
        var actionType = operation.ActionType.ToLowerInvariant();
        var targetType = operation.TargetType.ToLowerInvariant();

        try
        {
            if (targetType == "card")
            {
                return await ExecuteCardOperationAsync(actionType, operation, cancellationToken);
            }
            else if (targetType == "board")
            {
                return await ExecuteBoardOperationAsync(actionType, operation, cancellationToken);
            }
            else if (targetType == "column")
            {
                return await ExecuteColumnOperationAsync(actionType, operation, cancellationToken);
            }
            else
            {
                return Result.Failure(ErrorCodes.ValidationError, $"Unsupported target type: {targetType}");
            }
        }
        catch (Exception ex)
        {
            return Result.Failure(ErrorCodes.UnexpectedError, $"Operation execution failed: {ex.Message}");
        }
    }

    private async Task<Result> ExecuteCardOperationAsync(string actionType, ProposalOperationDto operation, CancellationToken cancellationToken)
    {
        if (!OperationParameterParser.TryDeserializeParameters(operation.Parameters, out var parameters, out var parseError))
            return Result.Failure(ErrorCodes.ValidationError, parseError);

        switch (actionType)
        {
            case "create":
                return await CreateCardAsync(parameters, operation.TargetId, cancellationToken);

            case "update":
                return await UpdateCardAsync(parameters, cancellationToken);

            case "move":
                return await MoveCardAsync(parameters, cancellationToken);

            case "archive":
                return await ArchiveCardAsync(parameters, cancellationToken);

            default:
                return Result.Failure(ErrorCodes.ValidationError, $"Unsupported card action: {actionType}");
        }
    }

    private async Task<Result> CreateCardAsync(
        JsonElement parameters,
        string? targetId,
        CancellationToken cancellationToken)
    {
        if (!OperationParameterParser.TryGetRequiredString(parameters, "title", out var title, out var titleError))
            return Result.Failure(ErrorCodes.ValidationError, titleError);

        var description = OperationParameterParser.GetOptionalString(parameters, "description");

        if (!OperationParameterParser.TryGetRequiredGuid(parameters, "columnId", out var columnId, out var columnIdError))
            return Result.Failure(ErrorCodes.ValidationError, columnIdError);

        if (!OperationParameterParser.TryGetRequiredGuid(parameters, "boardId", out var boardId, out var boardIdError))
            return Result.Failure(ErrorCodes.ValidationError, boardIdError);

        Guid? cardId = null;
        if (!string.IsNullOrWhiteSpace(targetId))
        {
            if (!Guid.TryParse(targetId, out var parsedTargetId))
                return Result.Failure(ErrorCodes.ValidationError, "Invalid targetId");

            cardId = parsedTargetId;
        }

        var dto = new CreateCardDto(boardId, columnId, title, description, null, null);
        var result = await _cardService.CreateCardAsync(dto, cardId, cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result> UpdateCardAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out var cardId, out var cardIdError))
            return Result.Failure(ErrorCodes.ValidationError, cardIdError);

        var title = OperationParameterParser.GetOptionalString(parameters, "title");
        var description = OperationParameterParser.GetOptionalString(parameters, "description");
        if (title == null && description == null)
            return Result.Failure(ErrorCodes.ValidationError, "Update card operation requires at least one of 'title' or 'description'");

        var dto = new UpdateCardDto(title, description, null, null, null, null);
        var result = await _cardService.UpdateCardAsync(cardId, dto, cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result> MoveCardAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out var cardId, out var cardIdError))
            return Result.Failure(ErrorCodes.ValidationError, cardIdError);

        if (!OperationParameterParser.TryGetRequiredGuid(parameters, "columnId", out var columnId, out var columnIdError))
            return Result.Failure(ErrorCodes.ValidationError, columnIdError);

        // Get current cards in target column to determine position
        var targetColumn = await _unitOfWork.Columns.GetByIdWithCardsAsync(columnId, cancellationToken);
        if (targetColumn == null)
            return Result.Failure(ErrorCodes.NotFound, $"Column {columnId} not found");

        var position = targetColumn.Cards.Any() ? targetColumn.Cards.Max(c => c.Position) + 1 : 0;
        var dto = new MoveCardDto(columnId, position);
        var result = await _cardService.MoveCardAsync(cardId, dto, cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result> ArchiveCardAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out var cardId, out var cardIdError))
            return Result.Failure(ErrorCodes.ValidationError, cardIdError);

        var dto = new UpdateCardDto(null, null, null, true, null, null);
        var result = await _cardService.UpdateCardAsync(cardId, dto, cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result> ExecuteBoardOperationAsync(string actionType, ProposalOperationDto operation, CancellationToken cancellationToken)
    {
        if (!OperationParameterParser.TryDeserializeParameters(operation.Parameters, out var parameters, out var parseError))
            return Result.Failure(ErrorCodes.ValidationError, parseError);

        switch (actionType)
        {
            case "update":
                return await UpdateBoardAsync(parameters, cancellationToken);

            default:
                return Result.Failure(ErrorCodes.ValidationError, $"Unsupported board action: {actionType}");
        }
    }

    private async Task<Result> UpdateBoardAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!OperationParameterParser.TryGetRequiredGuid(parameters, "boardId", out var boardId, out var boardIdError))
            return Result.Failure(ErrorCodes.ValidationError, boardIdError);

        var name = OperationParameterParser.GetOptionalString(parameters, "name");
        var description = OperationParameterParser.GetOptionalString(parameters, "description");
        var isArchived = OperationParameterParser.GetOptionalBoolean(parameters, "isArchived");
        if (name == null && description == null && !isArchived.HasValue)
            return Result.Failure(ErrorCodes.ValidationError, "Update board operation requires at least one of 'name', 'description', or 'isArchived'");

        var dto = new UpdateBoardDto(name, description, isArchived);
        var result = await _boardService.UpdateBoardAsync(boardId, dto, cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result> ExecuteColumnOperationAsync(string actionType, ProposalOperationDto operation, CancellationToken cancellationToken)
    {
        if (!OperationParameterParser.TryDeserializeParameters(operation.Parameters, out var parameters, out var parseError))
            return Result.Failure(ErrorCodes.ValidationError, parseError);

        switch (actionType)
        {
            case "reorder":
                return await ReorderColumnAsync(parameters, cancellationToken);

            default:
                return Result.Failure(ErrorCodes.ValidationError, $"Unsupported column action: {actionType}");
        }
    }

    private async Task<Result> ReorderColumnAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!OperationParameterParser.TryGetRequiredGuid(parameters, "columnId", out var columnId, out var columnIdError))
            return Result.Failure(ErrorCodes.ValidationError, columnIdError);

        if (!OperationParameterParser.TryGetRequiredInt32(parameters, "position", out var newPosition, out var positionError))
            return Result.Failure(ErrorCodes.ValidationError, positionError);

        if (newPosition < 0)
            return Result.Failure(ErrorCodes.ValidationError, "Invalid position: must be non-negative");

        var dto = new UpdateColumnDto(null, newPosition, null);
        var result = await _columnService.UpdateColumnAsync(columnId, dto, cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }
}
