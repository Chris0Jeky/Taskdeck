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
    internal const string ArchiveCardBlockReason = "Archived by an approved proposal.";

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

        var labelAction = CardLabelOperationVocabulary.Classify(actionType);
        if (labelAction == CardLabelOperationAction.Add)
            return await ChangeCardLabelAsync(parameters, add: true, cancellationToken);
        if (labelAction == CardLabelOperationAction.Remove)
            return await ChangeCardLabelAsync(parameters, add: false, cancellationToken);

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

        if (!OperationParameterParser.TryGetOptionalDateTimeOffset(
                parameters, "dueDate", out _, out var dueDate, out var dueDateError))
            return Result.Failure(ErrorCodes.ValidationError, dueDateError);

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

        if (!OperationParameterParser.TryGetOptionalStringArray(
                parameters, "labels", out var labelsProvided, out var labelNames, out var labelsError))
            return Result.Failure(ErrorCodes.ValidationError, labelsError);
        if (!OperationParameterParser.TryGetOptionalGuidArray(
                parameters, "labelIds", out var labelIdsProvided, out var suppliedLabelIds, out var labelIdsError))
            return Result.Failure(ErrorCodes.ValidationError, labelIdsError);

        var labelResolution = await ResolveLabelsAsync(
            boardId,
            labelsProvided,
            labelNames,
            labelIdsProvided,
            suppliedLabelIds,
            cancellationToken);
        if (!labelResolution.IsSuccess)
            return Result.Failure(labelResolution.ErrorCode, labelResolution.ErrorMessage);

        var dto = new CreateCardDto(boardId, columnId, title, description, dueDate, labelResolution.Value);
        var result = await _cardService.CreateCardAsync(dto, cardId, cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result> UpdateCardAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out var cardId, out var cardIdError))
            return Result.Failure(ErrorCodes.ValidationError, cardIdError);

        var title = OperationParameterParser.GetOptionalString(parameters, "title");
        var description = OperationParameterParser.GetOptionalString(parameters, "description");

        if (!OperationParameterParser.TryGetOptionalDateTimeOffset(
                parameters, "dueDate", out var dueDateProvided, out var dueDate, out var dueDateError))
            return Result.Failure(ErrorCodes.ValidationError, dueDateError);

        if (!OperationParameterParser.TryGetOptionalBoolean(
                parameters, "clearDueDate", out _, out var clearDueDate, out var clearDueDateError))
            return Result.Failure(ErrorCodes.ValidationError, clearDueDateError);

        if (dueDate.HasValue && clearDueDate)
            return Result.Failure(ErrorCodes.ValidationError, "Parameters 'dueDate' and 'clearDueDate' cannot both be specified");

        if (!OperationParameterParser.TryGetOptionalStringArray(
                parameters, "labels", out var labelsProvided, out var labelNames, out var labelsError))
            return Result.Failure(ErrorCodes.ValidationError, labelsError);
        if (!OperationParameterParser.TryGetOptionalGuidArray(
                parameters, "labelIds", out var labelIdsProvided, out var suppliedLabelIds, out var labelIdsError))
            return Result.Failure(ErrorCodes.ValidationError, labelIdsError);

        var shouldClearDueDate = clearDueDate || (dueDateProvided && !dueDate.HasValue);
        if (title == null && description == null && !dueDateProvided && !clearDueDate && !labelsProvided && !labelIdsProvided)
            return Result.Failure(
                ErrorCodes.ValidationError,
                "Update card operation requires at least one of 'title', 'description', 'dueDate', 'clearDueDate', 'labels', or 'labelIds'");

        List<Guid>? labelIds = null;
        if (labelsProvided || labelIdsProvided)
        {
            var card = await _unitOfWork.Cards.GetByIdAsync(cardId, cancellationToken);
            if (card == null)
                return Result.Failure(ErrorCodes.NotFound, $"Card {cardId} not found");

            var labelResolution = await ResolveLabelsAsync(
                card.BoardId,
                labelsProvided,
                labelNames,
                labelIdsProvided,
                suppliedLabelIds,
                cancellationToken);
            if (!labelResolution.IsSuccess)
                return Result.Failure(labelResolution.ErrorCode, labelResolution.ErrorMessage);

            labelIds = labelResolution.Value;
        }

        var dto = new UpdateCardDto(
            title,
            description,
            dueDate,
            null,
            null,
            labelIds,
            ClearDueDate: shouldClearDueDate);
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

        var dto = new UpdateCardDto(null, null, null, true, ArchiveCardBlockReason, null);
        var result = await _cardService.UpdateCardAsync(cardId, dto, cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result> ChangeCardLabelAsync(
        JsonElement parameters,
        bool add,
        CancellationToken cancellationToken)
    {
        if (!OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out var cardId, out var cardIdError))
            return Result.Failure(ErrorCodes.ValidationError, cardIdError);

        var card = await _unitOfWork.Cards.GetByIdWithLabelsAsync(cardId, cancellationToken);
        if (card == null)
            return Result.Failure(ErrorCodes.NotFound, $"Card {cardId} not found");

        var hasLabelId = parameters.TryGetProperty("labelId", out _);
        var hasLabelName = parameters.TryGetProperty("labelName", out _);
        if (hasLabelId == hasLabelName)
            return Result.Failure(ErrorCodes.ValidationError, "Provide exactly one of 'labelId' or 'labelName'");

        var labels = (await _unitOfWork.Labels.GetByBoardIdAsync(card.BoardId, cancellationToken)).ToList();
        Taskdeck.Domain.Entities.Label? label;
        if (hasLabelId)
        {
            if (!OperationParameterParser.TryGetRequiredGuid(parameters, "labelId", out var labelId, out var labelIdError))
                return Result.Failure(ErrorCodes.ValidationError, labelIdError);

            label = labels.FirstOrDefault(candidate => candidate.Id == labelId);
        }
        else
        {
            if (!OperationParameterParser.TryGetRequiredString(parameters, "labelName", out var labelName, out var labelNameError))
                return Result.Failure(ErrorCodes.ValidationError, labelNameError);

            var matchingLabels = labels
                .Where(candidate => string.Equals(candidate.Name, labelName, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            if (matchingLabels.Count > 1)
                return AmbiguousLabelFailure(labelName);

            label = matchingLabels.SingleOrDefault();
        }

        if (label == null)
            return Result.Failure(ErrorCodes.NotFound, "Label was not found on the card's board");

        var currentLabelIds = card.CardLabels.Select(cardLabel => cardLabel.LabelId).ToHashSet();
        var changed = add ? currentLabelIds.Add(label.Id) : currentLabelIds.Remove(label.Id);
        if (!changed)
            return Result.Success();

        var dto = new UpdateCardDto(null, null, null, null, null, currentLabelIds.ToList());
        var result = await _cardService.UpdateCardAsync(cardId, dto, cancellationToken);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result<List<Guid>?>> ResolveLabelsAsync(
        Guid boardId,
        bool namesProvided,
        IReadOnlyList<string> labelNames,
        bool idsProvided,
        IReadOnlyList<Guid> suppliedLabelIds,
        CancellationToken cancellationToken)
    {
        if (!namesProvided && !idsProvided)
            return Result.Success<List<Guid>?>(null);
        if (namesProvided && idsProvided)
            return Result.Failure<List<Guid>?>(ErrorCodes.ValidationError, "Provide exactly one of 'labels' or 'labelIds'");

        var labels = (await _unitOfWork.Labels.GetByBoardIdAsync(boardId, cancellationToken)).ToList();
        if (idsProvided)
        {
            foreach (var labelId in suppliedLabelIds.Distinct())
            {
                if (labels.All(candidate => candidate.Id != labelId))
                    return Result.Failure<List<Guid>?>(ErrorCodes.NotFound, "Label was not found on the card's board");
            }

            return Result.Success<List<Guid>?>(suppliedLabelIds.Distinct().ToList());
        }

        var resolvedIds = new List<Guid>();
        foreach (var labelName in labelNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var matchingLabels = labels
                .Where(candidate => string.Equals(candidate.Name, labelName, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            if (matchingLabels.Count > 1)
                return Result.Failure<List<Guid>?>(
                    ErrorCodes.ValidationError,
                    $"Label name '{labelName}' is ambiguous on the card's board; use a label ID");

            var label = matchingLabels.SingleOrDefault();
            if (label == null)
                return Result.Failure<List<Guid>?>(ErrorCodes.NotFound, $"Label '{labelName}' was not found on board {boardId}");

            resolvedIds.Add(label.Id);
        }

        return Result.Success<List<Guid>?>(resolvedIds);
    }

    private static Result AmbiguousLabelFailure(string labelName) => Result.Failure(
        ErrorCodes.ValidationError,
        $"Label name '{labelName}' is ambiguous on the card's board; use a label ID");

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
            case "create":
                return await CreateColumnAsync(operation, parameters, cancellationToken);

            case "reorder":
                return await ReorderColumnAsync(parameters, cancellationToken);

            default:
                return Result.Failure(ErrorCodes.ValidationError, $"Unsupported column action: {actionType}");
        }
    }

    private async Task<Result> CreateColumnAsync(
        ProposalOperationDto operation,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        // Preview/approve/apply all parse the same canonical payload. Recheck the
        // live position immediately inside execution as well, narrowing the
        // race between Apply's permission validation and the actual insert.
        var validationResult = await ProposalOperationContractValidator.ValidateCreateColumnForExecutionAsync(
            _unitOfWork,
            operation,
            parameters,
            cancellationToken);
        if (!validationResult.IsSuccess)
            return Result.Failure(validationResult.ErrorCode, validationResult.ErrorMessage);

        var contract = validationResult.Value;
        var result = await _columnService.CreateColumnAsync(
            new CreateColumnDto(
                contract.BoardId,
                contract.Name,
                contract.Position,
                contract.WipLimit),
            cancellationToken);

        return result.IsSuccess
            ? Result.Success()
            : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result> ReorderColumnAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!OperationParameterParser.TryGetRequiredGuid(parameters, "columnId", out var columnId, out var columnIdError))
            return Result.Failure(ErrorCodes.ValidationError, columnIdError);

        if (!OperationParameterParser.TryGetRequiredInt32(parameters, "position", out var newPosition, out var positionError))
            return Result.Failure(ErrorCodes.ValidationError, positionError);

        if (newPosition < 0)
            return Result.Failure(ErrorCodes.ValidationError, "Invalid position: must be non-negative");

        // Reorder is an atomic, lossless board reindex — it moves the column to the
        // requested slot without clearing its WipLimit or colliding with the unique
        // (BoardId, Position) index.
        var result = await _columnService.ReorderColumnAsync(columnId, newPosition, cancellationToken);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }
}
