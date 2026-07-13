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
    // These mirror the Card/Board domain aggregate limits so the shared preview
    // contract rejects exactly what Apply would reject (#1319 preview == apply).
    private const int MaxCardTitleLength = 200;
    private const int MaxCardDescriptionLength = 2000;
    private const int MaxBoardNameLength = 100;
    private const int MaxBoardDescriptionLength = 1000;

    public static async Task<Result> ValidateAsync(
        IUnitOfWork unitOfWork,
        Guid? proposalBoardId,
        IEnumerable<ProposalOperationDto> operations,
        CancellationToken cancellationToken = default)
    {
        var validationContext = new BoardValidationContext(unitOfWork, proposalBoardId);

        // Apply executes operations in Sequence order. Validate in that same order so
        // an operation may safely reference an entity created by an earlier step,
        // while references to not-yet-created entities still fail closed.
        foreach (var operation in operations.OrderBy(operation => operation.Sequence))
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

            var fieldResult = await ValidateOperationFieldsAsync(
                validationContext,
                operation,
                parameters,
                labelAction,
                cancellationToken);
            if (!fieldResult.IsSuccess)
                return fieldResult;

            if (operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) &&
                operation.ActionType.Equals("create", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(operation.TargetId, out var createdCardId))
            {
                validationContext.RegisterPlannedCard(createdCardId);
            }
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
                 operation.ActionType.Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            var cardResult = await validationContext.ValidateNewCardIdAsync(targetId.Value, cancellationToken);
            if (!cardResult.IsSuccess)
                return cardResult;
        }
        else if (targetId.HasValue &&
                 operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase))
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

    private static Task<Result> ValidateOperationFieldsAsync(
        BoardValidationContext validationContext,
        ProposalOperationDto operation,
        JsonElement parameters,
        CardLabelOperationAction labelAction,
        CancellationToken cancellationToken)
    {
        if (operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateCardFieldsAsync(
                validationContext,
                operation,
                parameters,
                labelAction,
                cancellationToken);
        }

        if (operation.TargetType.Equals("board", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(ValidateBoardFields(operation, parameters));

        if (operation.TargetType.Equals("column", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(ValidateColumnFields(operation, parameters));

        return Task.FromResult(Result.Failure(
            ErrorCodes.ValidationError,
            $"Unsupported target type: {operation.TargetType}"));
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
            if (normalizedAction.Equals("create", StringComparison.OrdinalIgnoreCase) &&
                !OperationParameterParser.TryGetRequiredString(parameters, "title", out _, out var titleError))
            {
                return Result.Failure(ErrorCodes.ValidationError, titleError);
            }

            if (normalizedAction.Equals("update", StringComparison.OrdinalIgnoreCase) &&
                !OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out _, out var cardIdError))
            {
                return Result.Failure(ErrorCodes.ValidationError, cardIdError);
            }

            // Enforce the Card aggregate string limits before preview so an
            // over-length title/description cannot preview successfully and then
            // fail during Apply.
            var titleValue = OperationParameterParser.GetOptionalString(parameters, "title");
            if (titleValue != null && titleValue.Length > MaxCardTitleLength)
                return Result.Failure(ErrorCodes.ValidationError, $"Card title cannot exceed {MaxCardTitleLength} characters");

            var descriptionValue = OperationParameterParser.GetOptionalString(parameters, "description");
            if (descriptionValue != null && descriptionValue.Length > MaxCardDescriptionLength)
                return Result.Failure(ErrorCodes.ValidationError, $"Card description cannot exceed {MaxCardDescriptionLength} characters");

            if (!OperationParameterParser.TryGetOptionalDateTimeOffset(
                    parameters, "dueDate", out var dueDateProvided, out var dueDate, out var dueDateError))
                return Result.Failure(ErrorCodes.ValidationError, dueDateError);

            if (!OperationParameterParser.TryGetOptionalBoolean(
                    parameters, "clearDueDate", out _, out var clearDueDate, out var clearDueDateError))
                return Result.Failure(ErrorCodes.ValidationError, clearDueDateError);

            if (dueDate.HasValue && clearDueDate)
                return Result.Failure(ErrorCodes.ValidationError, "Parameters 'dueDate' and 'clearDueDate' cannot both be specified");

            if (normalizedAction.Equals("create", StringComparison.OrdinalIgnoreCase))
            {
                if (!OperationParameterParser.TryGetRequiredGuid(parameters, "columnId", out _, out var columnIdError))
                    return Result.Failure(ErrorCodes.ValidationError, columnIdError);
                if (!OperationParameterParser.TryGetRequiredGuid(parameters, "boardId", out _, out var boardIdError))
                    return Result.Failure(ErrorCodes.ValidationError, boardIdError);
            }

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
                var matchingLabelCount = await validationContext.GetLabelNameMatchCountAsync(labelName, cancellationToken);
                if (matchingLabelCount == 0)
                    return Result.Failure(ErrorCodes.NotFound, "Label was not found on the proposal board");
                if (matchingLabelCount > 1)
                    return AmbiguousLabelFailure(labelName);
            }

            return Result.Success();
        }

        if (normalizedAction is "create" or "update")
            return Result.Success();

        if (normalizedAction is "move" or "archive")
        {
            if (!OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out _, out var cardIdError))
                return Result.Failure(ErrorCodes.ValidationError, cardIdError);

            if (normalizedAction == "move" &&
                !OperationParameterParser.TryGetRequiredGuid(parameters, "columnId", out _, out var columnIdError))
            {
                return Result.Failure(ErrorCodes.ValidationError, columnIdError);
            }

            return Result.Success();
        }

        return Result.Failure(
            ErrorCodes.ValidationError,
            $"Unsupported card action: {operation.ActionType}");
    }

    private static Result ValidateBoardFields(ProposalOperationDto operation, JsonElement parameters)
    {
        if (!operation.ActionType.Equals("update", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(
                ErrorCodes.ValidationError,
                $"Unsupported board action: {operation.ActionType}");
        }

        if (!OperationParameterParser.TryGetRequiredGuid(parameters, "boardId", out _, out var boardIdError))
            return Result.Failure(ErrorCodes.ValidationError, boardIdError);

        var name = OperationParameterParser.GetOptionalString(parameters, "name");
        var description = OperationParameterParser.GetOptionalString(parameters, "description");
        var isArchived = OperationParameterParser.GetOptionalBoolean(parameters, "isArchived");

        // Mirror the Board aggregate string limits before preview.
        if (name != null && name.Length > MaxBoardNameLength)
            return Result.Failure(ErrorCodes.ValidationError, $"Board name cannot exceed {MaxBoardNameLength} characters");
        if (description != null && description.Length > MaxBoardDescriptionLength)
            return Result.Failure(ErrorCodes.ValidationError, $"Board description cannot exceed {MaxBoardDescriptionLength} characters");

        return name == null && description == null && !isArchived.HasValue
            ? Result.Failure(
                ErrorCodes.ValidationError,
                "Update board operation requires at least one of 'name', 'description', or 'isArchived'")
            : Result.Success();
    }

    private static Result ValidateColumnFields(ProposalOperationDto operation, JsonElement parameters)
    {
        if (!operation.ActionType.Equals("reorder", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(
                ErrorCodes.ValidationError,
                $"Unsupported column action: {operation.ActionType}");
        }

        if (!OperationParameterParser.TryGetRequiredGuid(parameters, "columnId", out _, out var columnIdError))
            return Result.Failure(ErrorCodes.ValidationError, columnIdError);

        if (!OperationParameterParser.TryGetRequiredInt32(parameters, "position", out var position, out var positionError))
            return Result.Failure(ErrorCodes.ValidationError, positionError);

        return position < 0
            ? Result.Failure(ErrorCodes.ValidationError, "Invalid position: must be non-negative")
            : Result.Success();
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
                var matchingLabelCount = await validationContext.GetLabelNameMatchCountAsync(labelName, cancellationToken);
                if (matchingLabelCount == 0)
                    return Result.Failure(ErrorCodes.NotFound, $"Label '{labelName}' was not found on the proposal board");
                if (matchingLabelCount > 1)
                    return AmbiguousLabelFailure(labelName);
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
        private readonly HashSet<Guid> _plannedCardIds = [];
        private HashSet<Guid>? _labelIds;
        private Dictionary<string, int>? _labelNameCounts;

        public Guid? BoardId { get; } = boardId;

        public void RegisterPlannedCard(Guid cardId) => _plannedCardIds.Add(cardId);

        public async Task<Result> ValidateNewCardIdAsync(Guid cardId, CancellationToken cancellationToken)
        {
            // A create-card targetId of Guid.Empty parses successfully but the Card
            // aggregate rejects it at Apply. Reject it before preview so the approval
            // gate never registers an unusable planned card (#1319 preview == apply).
            if (cardId == Guid.Empty)
                return Result.Failure(ErrorCodes.ValidationError, "Create card targetId must be a non-empty identifier");

            if (_plannedCardIds.Contains(cardId))
                return Result.Failure(ErrorCodes.Conflict, "Create card targetId is duplicated within the proposal");

            if (!_cardBoardIds.TryGetValue(cardId, out var existingCardBoardId))
            {
                existingCardBoardId = (await unitOfWork.Cards.GetByIdAsync(cardId, cancellationToken))?.BoardId;
                _cardBoardIds[cardId] = existingCardBoardId;
            }

            return existingCardBoardId.HasValue
                ? Result.Failure(ErrorCodes.Conflict, "Create card targetId already exists")
                : Result.Success();
        }

        public async Task<Result> ValidateCardBoardAsync(Guid cardId, CancellationToken cancellationToken)
        {
            if (!BoardId.HasValue)
                return ScopeFailure("Operation card is outside the proposal board scope");

            if (_plannedCardIds.Contains(cardId))
                return Result.Success();

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

        public async Task<int> GetLabelNameMatchCountAsync(string labelName, CancellationToken cancellationToken)
        {
            await EnsureLabelsLoadedAsync(cancellationToken);
            return _labelNameCounts!.GetValueOrDefault(labelName);
        }

        private async Task EnsureLabelsLoadedAsync(CancellationToken cancellationToken)
        {
            if (_labelIds != null)
                return;

            var labels = (BoardId.HasValue
                ? await unitOfWork.Labels.GetByBoardIdAsync(BoardId.Value, cancellationToken)
                : []).ToList();
            _labelIds = labels.Select(label => label.Id).ToHashSet();
            _labelNameCounts = labels
                .GroupBy(label => label.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Result AmbiguousLabelFailure(string labelName) => Result.Failure(
        ErrorCodes.ValidationError,
        $"Label name '{labelName}' is ambiguous on the proposal board; use a label ID");

    private static Result ScopeFailure(string message) => Result.Failure(ErrorCodes.Forbidden, message);
}
