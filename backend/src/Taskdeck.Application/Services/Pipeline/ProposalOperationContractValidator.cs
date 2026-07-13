using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services.Pipeline;

/// <summary>
/// Validates the effective operation payload that preview and apply both consume.
/// This is deliberately separate from create-time shape validation: revisions may
/// change parameters after proposal creation, so entity scope and field semantics
/// must be re-established immediately before either trust boundary.
/// </summary>
public static class ProposalOperationContractValidator
{
    public static async Task<Result> ValidateAsync(
        IUnitOfWork unitOfWork,
        Guid? proposalBoardId,
        IEnumerable<ProposalOperationDto> operations,
        CancellationToken cancellationToken = default)
    {
        var validationContext = new BoardValidationContext(unitOfWork, proposalBoardId);

        foreach (var operation in operations)
        {
            if (!OperationParameterParser.TryDeserializeParameters(operation.Parameters, out var parameters, out var parseError))
                return Result.Failure(ErrorCodes.ValidationError, parseError);

            var labelAction = CardLabelOperationVocabulary.Classify(operation.ActionType);
            if (labelAction == CardLabelOperationAction.InvalidAlias)
            {
                return Result.Failure(
                    ErrorCodes.ValidationError,
                    $"Unsupported card label action alias: {operation.ActionType}");
            }

            if ((labelAction is CardLabelOperationAction.Add or CardLabelOperationAction.Remove) &&
                !operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure(
                    ErrorCodes.ValidationError,
                    $"Card label action '{operation.ActionType}' requires targetType 'card'");
            }

            var scopeResult = await ValidateEntityScopeAsync(
                validationContext,
                operation,
                parameters,
                cancellationToken);
            if (!scopeResult.IsSuccess)
                return scopeResult;

            var fieldResult = await ValidateCardFieldsAsync(
                validationContext,
                operation,
                parameters,
                labelAction,
                cancellationToken);
            if (!fieldResult.IsSuccess)
                return fieldResult;
        }

        return Result.Success();
    }

    private static async Task<Result> ValidateEntityScopeAsync(
        BoardValidationContext validationContext,
        ProposalOperationDto operation,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        Guid? parameterBoardId = null;
        if (parameters.TryGetProperty("boardId", out _))
        {
            if (!OperationParameterParser.TryGetRequiredGuid(parameters, "boardId", out var parsedBoardId, out var boardError))
                return Result.Failure(ErrorCodes.ValidationError, boardError);
            parameterBoardId = parsedBoardId;
        }

        if (parameterBoardId.HasValue &&
            (!validationContext.BoardId.HasValue || parameterBoardId != validationContext.BoardId))
            return ScopeFailure("Operation boardId is outside the proposal board scope");

        Guid? cardId = null;
        if (parameters.TryGetProperty("cardId", out _))
        {
            if (!OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out var parsedCardId, out var cardError))
                return Result.Failure(ErrorCodes.ValidationError, cardError);
            cardId = parsedCardId;
        }

        Guid? targetId = null;
        if (!string.IsNullOrWhiteSpace(operation.TargetId))
        {
            if (!Guid.TryParse(operation.TargetId, out var parsedTargetId))
                return Result.Failure(ErrorCodes.ValidationError, "Invalid targetId");
            targetId = parsedTargetId;
        }

        if (cardId.HasValue && targetId.HasValue &&
            operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) &&
            cardId != targetId)
        {
            return Result.Failure(ErrorCodes.ValidationError, "Operation targetId must match parameter 'cardId'");
        }

        if (cardId.HasValue)
        {
            var cardResult = await validationContext.ValidateCardBoardAsync(cardId.Value, cancellationToken);
            if (!cardResult.IsSuccess)
                return cardResult;
        }
        else if (targetId.HasValue &&
                 operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) &&
                 !operation.ActionType.Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            var cardResult = await validationContext.ValidateCardBoardAsync(targetId.Value, cancellationToken);
            if (!cardResult.IsSuccess)
                return cardResult;
        }

        foreach (var columnParameter in new[] { "columnId", "targetColumnId" })
        {
            if (!parameters.TryGetProperty(columnParameter, out _))
                continue;

            if (!OperationParameterParser.TryGetRequiredGuid(parameters, columnParameter, out var columnId, out var columnError))
                return Result.Failure(ErrorCodes.ValidationError, columnError);

            var columnResult = await validationContext.ValidateColumnBoardAsync(columnId, cancellationToken);
            if (!columnResult.IsSuccess)
                return columnResult;

            if (targetId.HasValue &&
                operation.TargetType.Equals("column", StringComparison.OrdinalIgnoreCase) &&
                columnId != targetId)
            {
                return Result.Failure(ErrorCodes.ValidationError, $"Operation targetId must match parameter '{columnParameter}'");
            }
        }

        if (targetId.HasValue && operation.TargetType.Equals("board", StringComparison.OrdinalIgnoreCase))
        {
            if (!validationContext.BoardId.HasValue || targetId != validationContext.BoardId ||
                (parameterBoardId.HasValue && targetId != parameterBoardId))
            {
                return ScopeFailure("Operation targetId is outside the proposal board scope");
            }
        }

        if (targetId.HasValue &&
            operation.TargetType.Equals("column", StringComparison.OrdinalIgnoreCase) &&
            !parameters.TryGetProperty("columnId", out _) &&
            !operation.ActionType.Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            var columnResult = await validationContext.ValidateColumnBoardAsync(targetId.Value, cancellationToken);
            if (!columnResult.IsSuccess)
                return columnResult;
        }

        return Result.Success();
    }

    private static async Task<Result> ValidateCardFieldsAsync(
        BoardValidationContext validationContext,
        ProposalOperationDto operation,
        JsonElement parameters,
        CardLabelOperationAction labelAction,
        CancellationToken cancellationToken)
    {
        if (!operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase))
            return Result.Success();

        var normalizedAction = operation.ActionType.ToLowerInvariant();
        if (normalizedAction.Equals("create", StringComparison.OrdinalIgnoreCase) ||
            normalizedAction.Equals("update", StringComparison.OrdinalIgnoreCase))
        {
            if (normalizedAction.Equals("update", StringComparison.OrdinalIgnoreCase) &&
                !OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out _, out var cardIdError))
            {
                return Result.Failure(ErrorCodes.ValidationError, cardIdError);
            }

            if (!OperationParameterParser.TryGetOptionalDateTimeOffset(
                    parameters, "dueDate", out var dueDateProvided, out var dueDate, out var dueDateError))
                return Result.Failure(ErrorCodes.ValidationError, dueDateError);

            if (!OperationParameterParser.TryGetOptionalBoolean(
                    parameters, "clearDueDate", out _, out var clearDueDate, out var clearDueDateError))
                return Result.Failure(ErrorCodes.ValidationError, clearDueDateError);

            if (dueDate.HasValue && clearDueDate)
                return Result.Failure(ErrorCodes.ValidationError, "Parameters 'dueDate' and 'clearDueDate' cannot both be specified");

            var labelsResult = await ValidateLabelsAsync(validationContext, parameters, cancellationToken);
            if (!labelsResult.IsSuccess)
                return labelsResult;

            if (normalizedAction.Equals("update", StringComparison.OrdinalIgnoreCase))
            {
                var title = OperationParameterParser.GetOptionalString(parameters, "title");
                var description = OperationParameterParser.GetOptionalString(parameters, "description");
                var labelsProvided = parameters.TryGetProperty("labels", out _);
                var labelIdsProvided = parameters.TryGetProperty("labelIds", out _);
                if (title == null && description == null && !dueDateProvided && !clearDueDate &&
                    !labelsProvided && !labelIdsProvided)
                {
                    return Result.Failure(
                        ErrorCodes.ValidationError,
                        "Update card operation requires at least one of 'title', 'description', 'dueDate', 'clearDueDate', 'labels', or 'labelIds'");
                }
            }
        }

        if (labelAction is CardLabelOperationAction.Add or CardLabelOperationAction.Remove)
        {
            if (!OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out _, out var cardIdError))
                return Result.Failure(ErrorCodes.ValidationError, cardIdError);

            var hasLabelId = parameters.TryGetProperty("labelId", out _);
            var hasLabelName = parameters.TryGetProperty("labelName", out _);
            if (hasLabelId == hasLabelName)
                return Result.Failure(ErrorCodes.ValidationError, "Provide exactly one of 'labelId' or 'labelName'");

            if (!validationContext.BoardId.HasValue)
                return ScopeFailure("Label operation requires a proposal board scope");

            if (hasLabelId)
            {
                if (!OperationParameterParser.TryGetRequiredGuid(parameters, "labelId", out var labelId, out var labelError))
                    return Result.Failure(ErrorCodes.ValidationError, labelError);
                if (!await validationContext.ContainsLabelIdAsync(labelId, cancellationToken))
                    return Result.Failure(ErrorCodes.NotFound, "Label was not found on the proposal board");
            }
            else
            {
                if (!OperationParameterParser.TryGetRequiredString(parameters, "labelName", out var labelName, out var labelError))
                    return Result.Failure(ErrorCodes.ValidationError, labelError);
                if (!await validationContext.ContainsLabelNameAsync(labelName, cancellationToken))
                    return Result.Failure(ErrorCodes.NotFound, "Label was not found on the proposal board");
            }
        }

        return Result.Success();
    }

    private static async Task<Result> ValidateLabelsAsync(
        BoardValidationContext validationContext,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        if (!OperationParameterParser.TryGetOptionalStringArray(
                parameters, "labels", out var namesProvided, out var labelNames, out var namesError))
            return Result.Failure(ErrorCodes.ValidationError, namesError);
        if (!OperationParameterParser.TryGetOptionalGuidArray(
                parameters, "labelIds", out var idsProvided, out var labelIds, out var idsError))
            return Result.Failure(ErrorCodes.ValidationError, idsError);

        if (!namesProvided && !idsProvided)
            return Result.Success();
        if (namesProvided && idsProvided)
            return Result.Failure(ErrorCodes.ValidationError, "Provide exactly one of 'labels' or 'labelIds'");
        if (!validationContext.BoardId.HasValue)
            return ScopeFailure("Card labels require a proposal board scope");

        if (namesProvided)
        {
            foreach (var labelName in labelNames)
            {
                if (!await validationContext.ContainsLabelNameAsync(labelName, cancellationToken))
                    return Result.Failure(ErrorCodes.NotFound, $"Label '{labelName}' was not found on the proposal board");
            }
        }
        else
        {
            foreach (var labelId in labelIds)
            {
                if (!await validationContext.ContainsLabelIdAsync(labelId, cancellationToken))
                    return Result.Failure(ErrorCodes.NotFound, "Label was not found on the proposal board");
            }
        }

        return Result.Success();
    }

    private sealed class BoardValidationContext(IUnitOfWork unitOfWork, Guid? boardId)
    {
        private readonly Dictionary<Guid, Guid?> _cardBoardIds = [];
        private readonly Dictionary<Guid, Guid?> _columnBoardIds = [];
        private HashSet<Guid>? _labelIds;
        private HashSet<string>? _labelNames;

        public Guid? BoardId { get; } = boardId;

        public async Task<Result> ValidateCardBoardAsync(Guid cardId, CancellationToken cancellationToken)
        {
            if (!BoardId.HasValue)
                return ScopeFailure("Operation card is outside the proposal board scope");

            if (!_cardBoardIds.TryGetValue(cardId, out var cardBoardId))
            {
                cardBoardId = (await unitOfWork.Cards.GetByIdAsync(cardId, cancellationToken))?.BoardId;
                _cardBoardIds[cardId] = cardBoardId;
            }

            return cardBoardId == BoardId
                ? Result.Success()
                : ScopeFailure("Operation card is outside the proposal board scope");
        }

        public async Task<Result> ValidateColumnBoardAsync(Guid columnId, CancellationToken cancellationToken)
        {
            if (!BoardId.HasValue)
                return ScopeFailure("Operation column is outside the proposal board scope");

            if (!_columnBoardIds.TryGetValue(columnId, out var columnBoardId))
            {
                columnBoardId = (await unitOfWork.Columns.GetByIdAsync(columnId, cancellationToken))?.BoardId;
                _columnBoardIds[columnId] = columnBoardId;
            }

            return columnBoardId == BoardId
                ? Result.Success()
                : ScopeFailure("Operation column is outside the proposal board scope");
        }

        public async Task<bool> ContainsLabelIdAsync(Guid labelId, CancellationToken cancellationToken)
        {
            await EnsureLabelsLoadedAsync(cancellationToken);
            return _labelIds!.Contains(labelId);
        }

        public async Task<bool> ContainsLabelNameAsync(string labelName, CancellationToken cancellationToken)
        {
            await EnsureLabelsLoadedAsync(cancellationToken);
            return _labelNames!.Contains(labelName);
        }

        private async Task EnsureLabelsLoadedAsync(CancellationToken cancellationToken)
        {
            if (_labelIds != null)
                return;

            var labels = (BoardId.HasValue
                ? await unitOfWork.Labels.GetByBoardIdAsync(BoardId.Value, cancellationToken)
                : []).ToList();
            _labelIds = labels.Select(label => label.Id).ToHashSet();
            _labelNames = labels.Select(label => label.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Result ScopeFailure(string message) => Result.Failure(ErrorCodes.Forbidden, message);
}
