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
        foreach (var operation in operations)
        {
            if (!OperationParameterParser.TryDeserializeParameters(operation.Parameters, out var parameters, out var parseError))
                return Result.Failure(ErrorCodes.ValidationError, parseError);

            var scopeResult = await ValidateEntityScopeAsync(
                unitOfWork,
                proposalBoardId,
                operation,
                parameters,
                cancellationToken);
            if (!scopeResult.IsSuccess)
                return scopeResult;

            var fieldResult = await ValidateCardFieldsAsync(
                unitOfWork,
                proposalBoardId,
                operation,
                parameters,
                cancellationToken);
            if (!fieldResult.IsSuccess)
                return fieldResult;
        }

        return Result.Success();
    }

    private static async Task<Result> ValidateEntityScopeAsync(
        IUnitOfWork unitOfWork,
        Guid? proposalBoardId,
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

        if (parameterBoardId.HasValue && (!proposalBoardId.HasValue || parameterBoardId != proposalBoardId))
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
            var cardResult = await ValidateCardBoardAsync(unitOfWork, proposalBoardId, cardId.Value, cancellationToken);
            if (!cardResult.IsSuccess)
                return cardResult;
        }
        else if (targetId.HasValue &&
                 operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) &&
                 !operation.ActionType.Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            var cardResult = await ValidateCardBoardAsync(unitOfWork, proposalBoardId, targetId.Value, cancellationToken);
            if (!cardResult.IsSuccess)
                return cardResult;
        }

        foreach (var columnParameter in new[] { "columnId", "targetColumnId" })
        {
            if (!parameters.TryGetProperty(columnParameter, out _))
                continue;

            if (!OperationParameterParser.TryGetRequiredGuid(parameters, columnParameter, out var columnId, out var columnError))
                return Result.Failure(ErrorCodes.ValidationError, columnError);

            if (!proposalBoardId.HasValue)
                return ScopeFailure("Operation column is outside the proposal board scope");
            var columns = await unitOfWork.Columns.GetByBoardIdAsync(proposalBoardId.Value, cancellationToken);
            if (columns.All(column => column.Id != columnId))
                return ScopeFailure("Operation column is outside the proposal board scope");

            if (targetId.HasValue &&
                operation.TargetType.Equals("column", StringComparison.OrdinalIgnoreCase) &&
                columnId != targetId)
            {
                return Result.Failure(ErrorCodes.ValidationError, $"Operation targetId must match parameter '{columnParameter}'");
            }
        }

        if (targetId.HasValue && operation.TargetType.Equals("board", StringComparison.OrdinalIgnoreCase))
        {
            if (!proposalBoardId.HasValue || targetId != proposalBoardId ||
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
            if (!proposalBoardId.HasValue)
                return ScopeFailure("Operation column is outside the proposal board scope");
            var columns = await unitOfWork.Columns.GetByBoardIdAsync(proposalBoardId.Value, cancellationToken);
            if (columns.All(column => column.Id != targetId.Value))
                return ScopeFailure("Operation column is outside the proposal board scope");
        }

        return Result.Success();
    }

    private static async Task<Result> ValidateCardFieldsAsync(
        IUnitOfWork unitOfWork,
        Guid? proposalBoardId,
        ProposalOperationDto operation,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        if (!operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase))
            return Result.Success();

        var normalizedAction = operation.ActionType
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .ToLowerInvariant();
        if (normalizedAction.Equals("create", StringComparison.OrdinalIgnoreCase) ||
            normalizedAction.Equals("update", StringComparison.OrdinalIgnoreCase))
        {
            if (!OperationParameterParser.TryGetOptionalDateTimeOffset(
                    parameters, "dueDate", out _, out var dueDate, out var dueDateError))
                return Result.Failure(ErrorCodes.ValidationError, dueDateError);

            if (!OperationParameterParser.TryGetOptionalBoolean(
                    parameters, "clearDueDate", out _, out var clearDueDate, out var clearDueDateError))
                return Result.Failure(ErrorCodes.ValidationError, clearDueDateError);

            if (dueDate.HasValue && clearDueDate)
                return Result.Failure(ErrorCodes.ValidationError, "Parameters 'dueDate' and 'clearDueDate' cannot both be specified");

            var labelsResult = await ValidateLabelsAsync(unitOfWork, proposalBoardId, parameters, cancellationToken);
            if (!labelsResult.IsSuccess)
                return labelsResult;
        }

        if (normalizedAction is "addlabel" or "removelabel")
        {
            var hasLabelId = parameters.TryGetProperty("labelId", out _);
            var hasLabelName = parameters.TryGetProperty("labelName", out _);
            if (hasLabelId == hasLabelName)
                return Result.Failure(ErrorCodes.ValidationError, "Provide exactly one of 'labelId' or 'labelName'");

            if (!proposalBoardId.HasValue)
                return ScopeFailure("Label operation requires a proposal board scope");

            var boardLabels = (await unitOfWork.Labels.GetByBoardIdAsync(proposalBoardId.Value, cancellationToken)).ToList();
            if (hasLabelId)
            {
                if (!OperationParameterParser.TryGetRequiredGuid(parameters, "labelId", out var labelId, out var labelError))
                    return Result.Failure(ErrorCodes.ValidationError, labelError);
                if (boardLabels.All(label => label.Id != labelId))
                    return Result.Failure(ErrorCodes.NotFound, "Label was not found on the proposal board");
            }
            else
            {
                if (!OperationParameterParser.TryGetRequiredString(parameters, "labelName", out var labelName, out var labelError))
                    return Result.Failure(ErrorCodes.ValidationError, labelError);
                if (boardLabels.All(label => !string.Equals(label.Name, labelName, StringComparison.OrdinalIgnoreCase)))
                    return Result.Failure(ErrorCodes.NotFound, "Label was not found on the proposal board");
            }
        }

        return Result.Success();
    }

    private static async Task<Result> ValidateLabelsAsync(
        IUnitOfWork unitOfWork,
        Guid? proposalBoardId,
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
        if (!proposalBoardId.HasValue)
            return ScopeFailure("Card labels require a proposal board scope");

        var boardLabels = (await unitOfWork.Labels.GetByBoardIdAsync(proposalBoardId.Value, cancellationToken)).ToList();
        if (namesProvided)
        {
            var missingName = labelNames.FirstOrDefault(name =>
                boardLabels.All(label => !string.Equals(label.Name, name, StringComparison.OrdinalIgnoreCase)));
            if (missingName != null)
                return Result.Failure(ErrorCodes.NotFound, $"Label '{missingName}' was not found on the proposal board");
        }
        else
        {
            var missingId = labelIds.FirstOrDefault(id => boardLabels.All(label => label.Id != id));
            if (missingId != Guid.Empty)
                return Result.Failure(ErrorCodes.NotFound, "Label was not found on the proposal board");
        }

        return Result.Success();
    }

    private static async Task<Result> ValidateCardBoardAsync(
        IUnitOfWork unitOfWork,
        Guid? proposalBoardId,
        Guid cardId,
        CancellationToken cancellationToken)
    {
        if (!proposalBoardId.HasValue)
            return ScopeFailure("Operation card is outside the proposal board scope");
        var cards = await unitOfWork.Cards.GetByBoardIdAsync(proposalBoardId.Value, cancellationToken);
        if (cards.All(card => card.Id != cardId))
            return ScopeFailure("Operation card is outside the proposal board scope");

        return Result.Success();
    }

    private static Result ScopeFailure(string message) => Result.Failure(ErrorCodes.Forbidden, message);
}
