using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services.Pipeline;

public static partial class ProposalOperationContractValidator
{
    // These mirror the Card/Board domain aggregate limits so the shared preview
    // contract rejects exactly what Apply would reject (#1319 preview == apply).
    private const int MaxCardTitleLength = 200;
    private const int MaxCardDescriptionLength = 2000;
    private const int MaxBoardNameLength = 100;
    private const int MaxBoardDescriptionLength = 1000;
    private const int MaxColumnNameLength = 50;
    private static readonly HashSet<string> CreateColumnParameterNames = new(StringComparer.Ordinal)
    {
        "boardId",
        "name",
        "position",
        "wipLimit"
    };

    // These fields are consumed only by card create/update handlers. Identity, column,
    // revision and singular label parameters are deliberately not part of this list.
    private static readonly string[] CardCreateUpdateParameterNames =
    [
        "title", "description", "dueDate", "clearDueDate", "labels", "labelIds",
        "workItemType", "parentCardId", "clearParent"
    ];

    /// <summary>
    /// Validates only the effective operation payload and its pure set-level rules.
    /// No entities, permissions, current revisions, archive state or capacity are read.
    /// The count is safe to expose in conflict evidence; the error message is not.
    /// </summary>
    public static Result ValidateShape(
        IReadOnlyCollection<ProposalOperationDto> operations,
        out int unevaluatedOperationCount)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var ordered = operations.Select((operation, index) => (Operation: operation, Index: index))
            .OrderBy(item => item.Operation.Sequence).ToArray();
        var parsed = new List<(int Index, ProposalOperationDto Operation, JsonElement Parameters)>();
        var invalid = new HashSet<int>();
        var plannedCardIds = new HashSet<Guid>();
        var firstFailure = Result.Success();

        void Record(Result result, IEnumerable<int> indices)
        {
            if (result.IsSuccess) return;
            if (firstFailure.IsSuccess) firstFailure = result;
            invalid.UnionWith(indices);
        }

        foreach (var (operation, index) in ordered)
        {
            var result = ValidateSingleShape(operation, plannedCardIds);
            Record(result, [index]);
            if (!ProposalOperationVocabulary.IsSupported(operation.TargetType, operation.ActionType) ||
                !OperationParameterParser.TryDeserializeParameters(operation.Parameters, out var parameters, out _))
                continue;
            parsed.Add((index, operation, parameters));
            if (result.IsSuccess && operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) &&
                operation.ActionType.Equals("create", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(operation.TargetId, out var createdCardId) && !plannedCardIds.Add(createdCardId))
                Record(Result.Failure(ErrorCodes.Conflict, "Create card id is duplicated within the proposal"), [index]);
        }

        var relations = parsed.Where(item => IsRelationOperation(item.Operation)).ToArray();
        if (relations.Length > 1)
            Record(Result.Failure(ErrorCodes.ValidationError, "A proposal may contain only one typed relation operation."),
                relations.Select(item => item.Index));
        var lifecycle = parsed.Where(item => IsRelationLifecycleMutation(item.Operation)).ToArray();
        if (relations.Length > 0 && lifecycle.Length > 0)
            Record(Result.Failure(ErrorCodes.ValidationError,
                "A typed relation operation cannot be combined with card archive, restore, or delete operations."),
                relations.Concat(lifecycle).Select(item => item.Index));

        var hierarchy = parsed.Where(item => ProposalHierarchyValidator.AffectsHierarchy(
            item.Operation.ActionType, item.Operation.TargetType, item.Parameters)).ToArray();
        if (hierarchy.Length > 1)
            Record(Result.Failure(ErrorCodes.ValidationError,
                "Use a separate proposal for each hierarchy-affecting operation on a board."), hierarchy.Select(item => item.Index));

        // Group by immutable target identity, not Sequence: duplicate sequence numbers
        // must not make another operation disappear from the exclusivity check.
        var cardOperations = parsed.Where(item => item.Operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase))
            .Select(item => (Item: item, CardId: ShapeCardId(item.Operation, item.Parameters)))
            .Where(item => item.CardId.HasValue).GroupBy(item => item.CardId!.Value);
        foreach (var group in cardOperations)
        {
            var items = group.Select(item => item.Item).ToArray();
            if (items.Length > 1 && items.Any(item => item.Operation.ActionType.Equals(ProposalAssignmentContract.Action, StringComparison.OrdinalIgnoreCase)))
                Record(Result.Failure(ErrorCodes.ValidationError,
                    "An assignment replacement must be the only operation on that card in a proposal."), items.Select(item => item.Index));
            var writes = items.Where(item => !item.Operation.ActionType.Equals("create", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (writes.Length > 1 && writes.Any(item => RequiresExclusiveCardWrite(item.Operation, item.Parameters)))
                Record(Result.Failure(ErrorCodes.ValidationError,
                    "Archive, restore, or work-item type change must be the only operation for that card in a proposal."), writes.Select(item => item.Index));
        }

        unevaluatedOperationCount = invalid.Count;
        return firstFailure;
    }

    private static Result ValidateSingleShape(ProposalOperationDto operation, IReadOnlySet<Guid> plannedCardIds)
    {
        var labelAction = CardLabelOperationVocabulary.Classify(operation.ActionType);
        if (labelAction == CardLabelOperationAction.InvalidAlias)
            return Result.Failure(ErrorCodes.ValidationError, $"Unsupported card label action alias: {operation.ActionType}");
        if ((labelAction is CardLabelOperationAction.Add or CardLabelOperationAction.Remove) &&
            !string.Equals(operation.TargetType, "card", StringComparison.OrdinalIgnoreCase))
            return Result.Failure(ErrorCodes.ValidationError, $"Card label action '{operation.ActionType}' requires targetType 'card'");
        if (!ProposalOperationVocabulary.IsSupported(operation.TargetType, operation.ActionType))
            return Result.Failure(ErrorCodes.ValidationError,
                ProposalOperationVocabulary.GetUnsupportedMessage(operation.TargetType ?? string.Empty, operation.ActionType ?? string.Empty));
        if (!OperationParameterParser.TryDeserializeParameters(operation.Parameters, out var parameters, out var error))
            return Result.Failure(ErrorCodes.ValidationError, error);

        var support = ValidateCardParameterSupport(operation);
        if (!support.IsSuccess) return support;
        if ((parameters.TryGetProperty("estimatedEffortMinutes", out _) || parameters.TryGetProperty("clearEstimatedEffort", out _)) &&
            (!operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) || operation.ActionType.ToLowerInvariant() is not ("create" or "update")))
            return Result.Failure(ErrorCodes.ValidationError, "Effort estimate parameters are supported only by card create and update operations");

        var identity = ValidateIdentityShape(operation, parameters);
        if (!identity.IsSuccess) return identity;
        var fields = operation.TargetType.ToLowerInvariant() switch
        {
            "card" => ValidateCardShape(plannedCardIds, operation, parameters, labelAction),
            "board" => ValidateBoardFields(operation, parameters),
            _ => ValidateColumnShape(operation, parameters)
        };
        if (!fields.IsSuccess) return fields;
        if (!operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase)) return Result.Success();

        if (ProposalHierarchyValidator.AffectsHierarchy(operation.ActionType, operation.TargetType, parameters))
        {
            if (!ProposalHierarchyValidator.TryReadParent(parameters, out _, out var clear, out error))
                return Result.Failure(ErrorCodes.ValidationError, error);
            if (operation.ActionType.Equals("create", StringComparison.OrdinalIgnoreCase) && clear)
                return Result.Failure(ErrorCodes.ValidationError, "A new card has no parent to remove.");
            if ((operation.ActionType.ToLowerInvariant() is "archive-lifecycle" or "delete") &&
                parameters.TryGetProperty("expectedChildrenFingerprint", out var fingerprint) && fingerprint.ValueKind != JsonValueKind.String)
                return Result.Failure(ErrorCodes.ValidationError, "expectedChildrenFingerprint must be a string.");
        }
        return RequiresExclusiveCardWrite(operation, parameters) ? ValidateExpectedTimestamp(parameters) : Result.Success();
    }

    private static Result ValidateIdentityShape(ProposalOperationDto operation, JsonElement parameters)
    {
        Guid? target = null;
        if (!string.IsNullOrWhiteSpace(operation.TargetId))
        {
            if (!Guid.TryParse(operation.TargetId, out var parsedTarget))
                return Result.Failure(ErrorCodes.ValidationError, "Invalid targetId");
            target = parsedTarget;
        }
        foreach (var name in new[] { "boardId", "cardId", "columnId", "targetColumnId" })
        {
            if (!parameters.TryGetProperty(name, out _)) continue;
            if (!OperationParameterParser.TryGetRequiredGuid(parameters, name, out var id, out var error))
                return Result.Failure(ErrorCodes.ValidationError, error);
            var matchesTargetType = name switch
            {
                "boardId" => operation.TargetType.Equals("board", StringComparison.OrdinalIgnoreCase),
                "cardId" => operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase),
                _ => operation.TargetType.Equals("column", StringComparison.OrdinalIgnoreCase)
            };
            if (target.HasValue && matchesTargetType && id != target)
                return name == "boardId" ? ScopeFailure("Operation targetId is outside the proposal board scope")
                    : Result.Failure(ErrorCodes.ValidationError, $"Operation targetId must match parameter '{name}'");
        }
        if (operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) &&
            operation.ActionType.Equals("create", StringComparison.OrdinalIgnoreCase) && target == Guid.Empty)
            return Result.Failure(ErrorCodes.ValidationError, "Create card id must be a non-empty identifier");
        return Result.Success();
    }

    private static Guid? ShapeCardId(ProposalOperationDto operation, JsonElement parameters) =>
        OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out var cardId, out _) ? cardId :
            Guid.TryParse(operation.TargetId, out var targetId) ? targetId : null;

    private static bool RequiresExclusiveCardWrite(ProposalOperationDto operation, JsonElement parameters) =>
        operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) &&
        (operation.ActionType.ToLowerInvariant() is "archive-lifecycle" or "restore-lifecycle" or "delete" ||
         operation.ActionType.Equals("update", StringComparison.OrdinalIgnoreCase) &&
         (parameters.TryGetProperty("workItemType", out _) || parameters.TryGetProperty("parentCardId", out _) || parameters.TryGetProperty("clearParent", out _)));

    private static Result ValidateExpectedTimestamp(JsonElement parameters) =>
        parameters.TryGetProperty("expectedUpdatedAt", out var timestamp) && timestamp.ValueKind == JsonValueKind.String && timestamp.TryGetDateTimeOffset(out _)
            ? Result.Success() : Result.Failure(ErrorCodes.ValidationError, "expectedUpdatedAt must be the card's displayed timestamp");

    private static Result ValidateCardParameterSupport(ProposalOperationDto operation)
    {
        if (!operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) ||
            operation.ActionType.ToLowerInvariant() is "create" or "update")
            return Result.Success();

        if (!OperationParameterParser.TryDeserializeParameters(operation.Parameters, out var parameters, out var error))
            return Result.Failure(ErrorCodes.ValidationError, error);

        foreach (var name in CardCreateUpdateParameterNames)
            if (parameters.TryGetProperty(name, out _))
                return Result.Failure(ErrorCodes.ValidationError,
                    $"Parameter '{name}' is not supported by card action '{operation.ActionType}'");

        return Result.Success();
    }

    private static bool IsRelationOperation(ProposalOperationDto operation) =>
        operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) &&
        operation.ActionType.Equals("add-relation", StringComparison.OrdinalIgnoreCase) ||
        operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) &&
        operation.ActionType.Equals("remove-relation", StringComparison.OrdinalIgnoreCase);

    private static bool IsRelationLifecycleMutation(ProposalOperationDto operation) =>
        operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) &&
        operation.ActionType.Equals("archive-lifecycle", StringComparison.OrdinalIgnoreCase) ||
        operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) &&
        operation.ActionType.Equals("restore-lifecycle", StringComparison.OrdinalIgnoreCase) ||
        operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) &&
        operation.ActionType.Equals("delete", StringComparison.OrdinalIgnoreCase);

    private static Result ValidateCardShape(
        IReadOnlySet<Guid> plannedCardIds,
        ProposalOperationDto operation,
        JsonElement parameters,
        CardLabelOperationAction labelAction)
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

            if (!OperationParameterParser.TryGetWorkItemType(parameters, out var workItemType, out var typeError))
                return Result.Failure(ErrorCodes.ValidationError, typeError);

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

            if (!OperationParameterParser.TryGetEstimatedEffortMinutes(parameters, out var estimatedEffortMinutes, out var estimateError))
                return Result.Failure(ErrorCodes.ValidationError, estimateError);
            if (!OperationParameterParser.TryGetOptionalBoolean(parameters, "clearEstimatedEffort", out var clearEstimateProvided, out var clearEstimatedEffort, out var clearEstimateError))
                return Result.Failure(ErrorCodes.ValidationError, clearEstimateError);
            if (normalizedAction == "create" && clearEstimateProvided)
                return Result.Failure(ErrorCodes.ValidationError, "Parameter 'clearEstimatedEffort' is supported only by card update operations");
            if (estimatedEffortMinutes.HasValue && clearEstimatedEffort)
                return Result.Failure(ErrorCodes.ValidationError, "Parameters 'estimatedEffortMinutes' and 'clearEstimatedEffort' cannot both be specified");
            if (normalizedAction == "update" && (estimatedEffortMinutes.HasValue || clearEstimatedEffort))
            {
                OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out var estimateCardId, out _);
                if (!plannedCardIds.Contains(estimateCardId))
                {
                    var estimateVersion = ValidateExpectedTimestamp(parameters);
                    if (!estimateVersion.IsSuccess) return estimateVersion;
                }
            }

            if (normalizedAction.Equals("create", StringComparison.OrdinalIgnoreCase))
            {
                if (!OperationParameterParser.TryGetRequiredGuid(parameters, "columnId", out _, out var columnIdError))
                    return Result.Failure(ErrorCodes.ValidationError, columnIdError);
                if (!OperationParameterParser.TryGetRequiredGuid(parameters, "boardId", out _, out var boardIdError))
                    return Result.Failure(ErrorCodes.ValidationError, boardIdError);
            }

            var labelsResult = ValidateLabelsShape(parameters);
            if (!labelsResult.IsSuccess)
                return labelsResult;

            if (normalizedAction.Equals("update", StringComparison.OrdinalIgnoreCase))
            {
                var title = OperationParameterParser.GetOptionalString(parameters, "title");
                var description = OperationParameterParser.GetOptionalString(parameters, "description");
                var labelsProvided = parameters.TryGetProperty("labels", out _);
                var labelIdsProvided = parameters.TryGetProperty("labelIds", out _);
                if (title == null && description == null && !dueDateProvided && !clearDueDate &&
                    !labelsProvided && !labelIdsProvided && workItemType is null && !estimatedEffortMinutes.HasValue && !clearEstimatedEffort && !parameters.TryGetProperty("parentCardId", out _) && !parameters.TryGetProperty("clearParent", out _))
                {
                    return Result.Failure(
                        ErrorCodes.ValidationError,
                        "Update card operation requires at least one of 'title', 'description', 'dueDate', 'clearDueDate', 'labels', 'labelIds', 'workItemType', 'estimatedEffortMinutes', or 'clearEstimatedEffort'");
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

            return hasLabelId
                ? OperationParameterParser.TryGetRequiredGuid(parameters, "labelId", out _, out var labelIdError)
                    ? Result.Success() : Result.Failure(ErrorCodes.ValidationError, labelIdError)
                : OperationParameterParser.TryGetRequiredString(parameters, "labelName", out _, out var labelNameError)
                    ? Result.Success() : Result.Failure(ErrorCodes.ValidationError, labelNameError);
        }

        if (normalizedAction is "create" or "update")
            return Result.Success();

        if (normalizedAction == ProposalAssignmentContract.Action)
        {
            try { ProposalAssignmentContract.Read(parameters); }
            catch (DomainException ex) { return Result.Failure(ex.ErrorCode, ex.Message); }
            return OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out _, out var assignmentError)
                ? Result.Success() : Result.Failure(ErrorCodes.ValidationError, assignmentError);
        }

        if (normalizedAction is "add-relation" or "remove-relation")
        {
            return OperationParameterParser.TryGetRelationOperationParameters(parameters, out _, out var relationError)
                ? Result.Success()
                : Result.Failure(ErrorCodes.ValidationError, relationError);
        }

        if (normalizedAction is "move" or "archive" or "archive-lifecycle" or "restore-lifecycle" or "delete")
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

    private static Result ValidateColumnShape(
        ProposalOperationDto operation,
        JsonElement parameters)
    {
        if (operation.ActionType.Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            var contractResult = ParseCreateColumnParameters(operation, parameters);
            if (!contractResult.IsSuccess)
                return Result.Failure(contractResult.ErrorCode, contractResult.ErrorMessage);

            return Result.Success();
        }

        if (operation.ActionType.Equals("reorder", StringComparison.OrdinalIgnoreCase))
        {
            if (!OperationParameterParser.TryGetRequiredGuid(parameters, "columnId", out _, out var columnIdError))
                return Result.Failure(ErrorCodes.ValidationError, columnIdError);

            if (!OperationParameterParser.TryGetRequiredInt32(parameters, "position", out var position, out var positionError))
                return Result.Failure(ErrorCodes.ValidationError, positionError);

            return position < 0
                ? Result.Failure(ErrorCodes.ValidationError, "Invalid position: must be non-negative")
                : Result.Success();
        }

        return Result.Failure(
            ErrorCodes.ValidationError,
            $"Unsupported column action: {operation.ActionType}");
    }

    private static Result ValidateLabelsShape(JsonElement parameters)
    {
        if (!OperationParameterParser.TryGetOptionalStringArray(parameters, "labels", out var namesProvided, out _, out var namesError))
            return Result.Failure(ErrorCodes.ValidationError, namesError);
        if (!OperationParameterParser.TryGetOptionalGuidArray(parameters, "labelIds", out var idsProvided, out _, out var idsError))
            return Result.Failure(ErrorCodes.ValidationError, idsError);
        return namesProvided && idsProvided
            ? Result.Failure(ErrorCodes.ValidationError, "Provide exactly one of 'labels' or 'labelIds'")
            : Result.Success();
    }

    internal static Result<CreateColumnOperationParameters> ParseCreateColumnParameters(
        ProposalOperationDto operation,
        JsonElement parameters)
    {
        if (operation.TargetId is not null)
        {
            return Result.Failure<CreateColumnOperationParameters>(
                ErrorCodes.ValidationError,
                "Create column operation must not specify targetId");
        }

        var seenParameterNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in parameters.EnumerateObject())
        {
            if (!CreateColumnParameterNames.Contains(property.Name))
            {
                return Result.Failure<CreateColumnOperationParameters>(
                    ErrorCodes.ValidationError,
                    $"Unsupported create column parameter '{property.Name}'");
            }

            if (!seenParameterNames.Add(property.Name))
            {
                return Result.Failure<CreateColumnOperationParameters>(
                    ErrorCodes.ValidationError,
                    $"Duplicate create column parameter '{property.Name}'");
            }
        }

        if (!OperationParameterParser.TryGetRequiredGuid(parameters, "boardId", out var boardId, out var boardIdError))
            return Result.Failure<CreateColumnOperationParameters>(ErrorCodes.ValidationError, boardIdError);
        if (boardId == Guid.Empty)
        {
            return Result.Failure<CreateColumnOperationParameters>(
                ErrorCodes.ValidationError,
                "Parameter 'boardId' must be a non-empty identifier");
        }

        if (!OperationParameterParser.TryGetRequiredString(parameters, "name", out var name, out var nameError))
            return Result.Failure<CreateColumnOperationParameters>(ErrorCodes.ValidationError, nameError);
        if (name.Length > MaxColumnNameLength)
        {
            return Result.Failure<CreateColumnOperationParameters>(
                ErrorCodes.ValidationError,
                $"Column name cannot exceed {MaxColumnNameLength} characters");
        }

        if (!OperationParameterParser.TryGetRequiredInt32(parameters, "position", out var position, out var positionError))
            return Result.Failure<CreateColumnOperationParameters>(ErrorCodes.ValidationError, positionError);
        if (position < 0)
        {
            return Result.Failure<CreateColumnOperationParameters>(
                ErrorCodes.ValidationError,
                "Invalid position: must be non-negative");
        }

        int? wipLimit = null;
        if (parameters.TryGetProperty("wipLimit", out var wipLimitProperty) &&
            wipLimitProperty.ValueKind != JsonValueKind.Null)
        {
            if (wipLimitProperty.ValueKind != JsonValueKind.Number ||
                !wipLimitProperty.TryGetInt32(out var parsedWipLimit))
            {
                return Result.Failure<CreateColumnOperationParameters>(
                    ErrorCodes.ValidationError,
                    "Parameter 'wipLimit' must be an integer or null");
            }

            if (parsedWipLimit <= 0)
            {
                return Result.Failure<CreateColumnOperationParameters>(
                    ErrorCodes.ValidationError,
                    "WIP limit must be greater than 0");
            }

            wipLimit = parsedWipLimit;
        }

        return Result.Success(new CreateColumnOperationParameters(boardId, name, position, wipLimit));
    }

}
