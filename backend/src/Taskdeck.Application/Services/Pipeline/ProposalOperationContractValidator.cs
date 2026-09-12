using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
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
    private const int MaxColumnNameLength = 50;
    private static readonly HashSet<string> CreateColumnParameterNames = new(StringComparer.Ordinal)
    {
        "boardId",
        "name",
        "position",
        "wipLimit"
    };

    public static async Task<Result> ValidateAsync(
        IUnitOfWork unitOfWork,
        Guid? proposalBoardId,
        IEnumerable<ProposalOperationDto> operations,
        CancellationToken cancellationToken = default,
        IBoardDependencyRepository? dependencies = null)
    {
        var materializedOperations = operations.ToList();
        var relationOperations = materializedOperations.Where(IsRelationOperation).ToList();
        if (relationOperations.Count > 1)
            return Result.Failure(ErrorCodes.ValidationError, "A proposal may contain only one typed relation operation.");
        if (relationOperations.Count == 1 && materializedOperations.Any(IsRelationLifecycleMutation))
        {
            return Result.Failure(
                ErrorCodes.ValidationError,
                "A typed relation operation cannot be combined with card archive, restore, or delete operations.");
        }
        var hierarchyResult = await ProposalHierarchyValidator.ValidateAsync(unitOfWork, proposalBoardId, materializedOperations, cancellationToken);
        if (!hierarchyResult.IsSuccess) return Result.Failure(hierarchyResult.ErrorCode, hierarchyResult.ErrorMessage);
        var validationContext = new BoardValidationContext(unitOfWork, proposalBoardId);

        // Apply executes operations in Sequence order. Validate in that same order so
        // an operation may safely reference an entity created by an earlier step,
        // while references to not-yet-created entities still fail closed.
        foreach (var operation in materializedOperations.OrderBy(operation => operation.Sequence))
        {
            if (operation.ActionType.Equals(ProposalAssignmentContract.Action, StringComparison.OrdinalIgnoreCase))
            {
                if (!operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) ||
                    !OperationParameterParser.TryDeserializeParameters(operation.Parameters, out var assignmentParameters, out _))
                    return Result.Failure(ErrorCodes.ValidationError, "Assignment operation requires a card and valid parameters.");
                var assignment = await ProposalAssignmentContract.ValidateAsync(unitOfWork, proposalBoardId, assignmentParameters, cancellationToken);
                if (!assignment.IsSuccess) return Result.Failure(assignment.ErrorCode, assignment.ErrorMessage);
                var assignmentCardId = assignmentParameters.GetProperty("cardId").GetGuid();
                foreach (var other in materializedOperations.Where(other => !ReferenceEquals(other, operation) &&
                             other.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase)))
                {
                    if (Guid.TryParse(other.TargetId, out var target) && target == assignmentCardId ||
                        OperationParameterParser.TryDeserializeParameters(other.Parameters, out var otherParameters, out _) &&
                        OperationParameterParser.TryGetRequiredGuid(otherParameters, "cardId", out var otherCardId, out _) && otherCardId == assignmentCardId)
                        return Result.Failure(ErrorCodes.ValidationError,
                            "An assignment replacement must be the only operation on that card in a proposal.");
                }
            }
            if (!OperationParameterParser.TryDeserializeParameters(operation.Parameters, out var parameters, out var parseError))
                return Result.Failure(ErrorCodes.ValidationError, parseError);

            if ((parameters.TryGetProperty("estimatedEffortMinutes", out _) || parameters.TryGetProperty("clearEstimatedEffort", out _)) &&
                (!operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) ||
                 operation.ActionType.ToLowerInvariant() is not ("create" or "update")))
                return Result.Failure(ErrorCodes.ValidationError, "Effort estimate parameters are supported only by card create and update operations");

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

            if (IsRelationOperation(operation))
            {
                var relationResult = await ValidateRelationOperationAsync(
                    validationContext,
                    operation.ActionType,
                    parameters,
                    dependencies,
                    cancellationToken);
                if (!relationResult.IsSuccess)
                    return relationResult;
            }

            var fieldResult = await ValidateOperationFieldsAsync(
                validationContext,
                operation,
                parameters,
                labelAction,
                cancellationToken);
            if (!fieldResult.IsSuccess)
                return fieldResult;

            var cardStateResult = await validationContext.ValidateCardArchiveStateAsync(operation, parameters, cancellationToken);
            if (!cardStateResult.IsSuccess) return cardStateResult;

            var archiveStateResult = validationContext.ValidateOperationAfterPlannedBoardArchive(operation, parameters);
            if (!archiveStateResult.IsSuccess)
                return archiveStateResult;

            var capacityResult = await validationContext.ValidateIncomingCardCapacityAsync(operation, parameters, cancellationToken);
            if (!capacityResult.IsSuccess)
                return capacityResult;

            validationContext.ApplyPlannedBoardArchiveState(operation, parameters);

            if (operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) &&
                operation.ActionType.Equals("create", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(operation.TargetId, out var createdCardId))
            {
                validationContext.RegisterPlannedCard(createdCardId);
            }

            // Record this operation's effect on column occupancy only after it has been
            // accepted, so every later create, move or restore is measured against the
            // board Apply will actually see at that point (#2926, #3020).
            await validationContext.ProjectColumnOccupancyAsync(operation, parameters, cancellationToken);
        }

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

    private static async Task<Result> ValidateRelationOperationAsync(
        BoardValidationContext validationContext,
        string actionType,
        JsonElement parameters,
        IBoardDependencyRepository? dependencies,
        CancellationToken cancellationToken)
    {
        if (!OperationParameterParser.TryGetRelationOperationParameters(parameters, out var relationParameters, out var error))
            return Result.Failure(ErrorCodes.ValidationError, error);
        if (!validationContext.BoardId.HasValue || relationParameters.BoardId != validationContext.BoardId.Value)
            return ScopeFailure("Operation boardId is outside the proposal board scope");
        if (dependencies is null)
        {
            return Result.Failure(
                ErrorCodes.UnexpectedError,
                "Typed relation validation is unavailable.");
        }

        return await validationContext.ValidateRelationEndpointsAsync(
            relationParameters.Relation,
            relationParameters.ExpectedRevision,
            actionType.Equals("remove-relation", StringComparison.OrdinalIgnoreCase),
            dependencies,
            cancellationToken);
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

        var isCardCreate = operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) &&
                           operation.ActionType.Equals("create", StringComparison.OrdinalIgnoreCase);

        if (isCardCreate)
        {
            // Apply sources the created card's id exclusively from operation.TargetId
            // (OperationHandlerRegistry.CreateCardAsync ignores a cardId parameter; a
            // cardId parameter that disagrees with targetId is already rejected above).
            // Validate exactly that id as a NEW card id, so a collision with an existing
            // card fails at preview instead of during Apply, and never route a create op
            // through the existing-card branch, which would treat the colliding id as a
            // valid reference (#1370 preview == apply). When TargetId is absent Apply
            // generates a fresh id, so there is nothing to collision-check.
            if (targetId.HasValue)
            {
                var cardResult = await validationContext.ValidateNewCardIdAsync(targetId.Value, cancellationToken);
                if (!cardResult.IsSuccess)
                    return cardResult;
            }
        }
        else if (cardId.HasValue)
        {
            var cardResult = await validationContext.ValidateCardBoardAsync(cardId.Value, cancellationToken);
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
        {
            return ValidateColumnFieldsAsync(
                validationContext,
                operation,
                parameters,
                cancellationToken);
        }

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

        // Only the create and update card handlers read 'workItemType'
        // (OperationHandlerRegistry.CreateCardAsync / UpdateCardAsync); move, archive,
        // the lifecycle verbs, delete, assignment replacement and the label verbs all
        // ignore it at Apply. Accepting it on those actions let the approval preview
        // announce a "Work item type: Task -> Epic" transition that Apply never performs,
        // so reject it here in the shared preview/apply gate instead (#2950 preview == apply).
        if (normalizedAction is not ("create" or "update") && parameters.TryGetProperty("workItemType", out _))
        {
            return Result.Failure(
                ErrorCodes.ValidationError,
                $"Parameter 'workItemType' is not supported by card action '{operation.ActionType}'");
        }

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
                var estimateVersion = await validationContext.ValidateEstimateVersionAsync(parameters, cancellationToken);
                if (!estimateVersion.IsSuccess) return estimateVersion;
            }

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

    private static async Task<Result> ValidateColumnFieldsAsync(
        BoardValidationContext validationContext,
        ProposalOperationDto operation,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        if (operation.ActionType.Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            var contractResult = ParseCreateColumnParameters(operation, parameters);
            if (!contractResult.IsSuccess)
                return Result.Failure(contractResult.ErrorCode, contractResult.ErrorMessage);

            return await validationContext.ValidateAndRegisterNewColumnAsync(
                contractResult.Value,
                cancellationToken);
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

    internal static Result ValidateCreateColumnPositionAvailability(
        IEnumerable<Taskdeck.Domain.Entities.Column> columns,
        CreateColumnOperationParameters parameters)
    {
        if (columns.Any(column => column.Position == parameters.Position))
        {
            return Result.Failure(
                ErrorCodes.Conflict,
                $"Column position {parameters.Position} is already occupied on board {parameters.BoardId}");
        }

        return Result.Success();
    }

    internal static Result<int> ResolveAppendPosition(IEnumerable<Taskdeck.Domain.Entities.Column> columns)
    {
        var materializedColumns = columns as IReadOnlyCollection<Taskdeck.Domain.Entities.Column> ?? columns.ToList();
        if (materializedColumns.Count == 0)
            return Result.Success(0);

        var maximumPosition = materializedColumns.Max(column => column.Position);
        return maximumPosition == int.MaxValue
            ? Result.Failure<int>(ErrorCodes.Conflict, "Cannot append a column because the board has no higher position available")
            : Result.Success(maximumPosition + 1);
    }

    internal static async Task<Result<CreateColumnOperationParameters>> ValidateCreateColumnForExecutionAsync(
        IUnitOfWork unitOfWork,
        ProposalOperationDto operation,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        var contractResult = ParseCreateColumnParameters(operation, parameters);
        if (!contractResult.IsSuccess)
        {
            return Result.Failure<CreateColumnOperationParameters>(
                contractResult.ErrorCode,
                contractResult.ErrorMessage);
        }

        var columns = await unitOfWork.Columns.GetByBoardIdAsync(
            contractResult.Value.BoardId,
            cancellationToken);
        var availabilityResult = ValidateCreateColumnPositionAvailability(columns, contractResult.Value);
        return availabilityResult.IsSuccess
            ? contractResult
            : Result.Failure<CreateColumnOperationParameters>(
                availabilityResult.ErrorCode,
                availabilityResult.ErrorMessage);
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
        private readonly Dictionary<Guid, Taskdeck.Domain.Entities.Card?> _cards = [];

        private async Task<Taskdeck.Domain.Entities.Card?> ReadCardAsync(Guid cardId, CancellationToken ct)
        {
            if (!_cards.TryGetValue(cardId, out var card))
            {
                card = await unitOfWork.Cards.GetByIdAsync(cardId, ct);
                _cards[cardId] = card;
            }
            return card;
        }
        private readonly Dictionary<Guid, Guid?> _columnBoardIds = [];
        private readonly HashSet<Guid> _plannedCardIds = [];
        private readonly HashSet<int> _plannedColumnPositions = [];
        private IReadOnlyCollection<Taskdeck.Domain.Entities.Column>? _boardColumns;
        private HashSet<Guid>? _labelIds;
        private Dictionary<string, int>? _labelNameCounts;

        public Guid? BoardId { get; } = boardId;
        private bool _isBoardArchivedInProposal;
        private readonly HashSet<Guid> _mutatedCards = [];
        private readonly HashSet<Guid> _lifecycleCards = [];

        // Ordered projection of what this proposal does to each column's active-card count,
        // plus the column each card is projected to occupy once the preceding operations have
        // run. Apply mutates the board operation by operation, so each capacity check has to
        // be measured against that moving count rather than the snapshot the proposal started
        // from (#2926, #3020).
        private readonly Dictionary<Guid, int> _projectedColumnActiveDelta = [];
        private readonly Dictionary<Guid, Guid> _projectedCardColumns = [];
        private readonly Dictionary<Guid, Taskdeck.Domain.Entities.Column?> _columnsWithCards = [];

        public async Task<Result> ValidateCardArchiveStateAsync(ProposalOperationDto operation, JsonElement parameters, CancellationToken ct)
        {
            if (!operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) ||
                operation.ActionType.Equals("create", StringComparison.OrdinalIgnoreCase)) return Result.Success();
            if (!OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out var cardId, out _)) return Result.Success();
            var action = operation.ActionType.ToLowerInvariant();
            var lifecycle = action is "archive-lifecycle" or "restore-lifecycle";
            var typeChange = action == "update" && (parameters.TryGetProperty("workItemType", out _) || parameters.TryGetProperty("parentCardId", out _) || parameters.TryGetProperty("clearParent", out _));
            var pinned = lifecycle || typeChange || action == "delete";
            // A lifecycle approval pins one exact card revision. Mixing another write to that
            // card would invalidate its timestamp during Apply; reject this at Preview too.
            if (_lifecycleCards.Contains(cardId) || (pinned && _mutatedCards.Contains(cardId)))
                return Result.Failure(ErrorCodes.ValidationError, "Archive, restore, or work-item type change must be the only operation for that card in a proposal.");
            _mutatedCards.Add(cardId);
            if (pinned) _lifecycleCards.Add(cardId);
            var card = await ReadCardAsync(cardId, ct);
            if (card is null) return pinned
                ? Result.Failure(ErrorCodes.NotFound, "Archive, restore, and type changes require an existing card") : Result.Success();
            if (!pinned) return card.IsArchived
                ? Result.Failure(ErrorCodes.InvalidOperation, "Card is archived. Restore it before editing.") : Result.Success();
            if (!parameters.TryGetProperty("expectedUpdatedAt", out var timestamp) ||
                timestamp.ValueKind != JsonValueKind.String || !timestamp.TryGetDateTimeOffset(out var expected))
                return Result.Failure(ErrorCodes.ValidationError, "expectedUpdatedAt must be the card's displayed timestamp");
            if (card.UpdatedAt != expected)
                return Result.Failure(ErrorCodes.Conflict, "Card changed since this proposal was prepared. Refresh and create a new proposal.");
            if (action == "delete") return Result.Success();
            if (typeChange) return card.IsArchived
                ? Result.Failure(ErrorCodes.InvalidOperation, "Card is archived. Restore it before editing.") : Result.Success();
            var archive = action == "archive-lifecycle";
            if (card.IsArchived == archive)
                return Result.Failure(ErrorCodes.InvalidOperation, archive ? "Card is already archived" : "Card is already active");
            if (!archive)
            {
                var column = await unitOfWork.Columns.GetByIdWithCardsAsync(card.ColumnId, ct);
                if (column is null || column.BoardId != card.BoardId)
                    return Result.Failure(ErrorCodes.InvalidOperation, "Restore the original column before restoring this card.");
                if (WouldProjectedAddExceedWipLimit(column))
                    return Result.Failure(ErrorCodes.WipLimitExceeded, "The original column is full. Free space or adjust its WIP limit before restoring this card.");
            }
            return Result.Success();
        }

        public void RegisterPlannedCard(Guid cardId) => _plannedCardIds.Add(cardId);

        public async Task<Result> ValidateEstimateVersionAsync(JsonElement parameters, CancellationToken ct)
        {
            if (!OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out var cardId, out var error))
                return Result.Failure(ErrorCodes.ValidationError, error);
            // A card created earlier in this proposal has no persisted pre-proposal version.
            if (_plannedCardIds.Contains(cardId)) return Result.Success();
            if (!parameters.TryGetProperty("expectedUpdatedAt", out var timestamp) ||
                timestamp.ValueKind != JsonValueKind.String || !timestamp.TryGetDateTimeOffset(out var expected))
                return Result.Failure(ErrorCodes.ValidationError, "expectedUpdatedAt must be the card's displayed timestamp");
            var card = await ReadCardAsync(cardId, ct);
            if (card is null) return Result.Failure(ErrorCodes.NotFound, "Card not found");
            // Every operation is checked before any apply-time mutation. Repeated estimate
            // changes pin this same initial state, not hypothetical future timestamps.
            return card.UpdatedAt == expected ? Result.Success()
                : Result.Failure(ErrorCodes.Conflict, "Card changed since this proposal was prepared. Refresh and create a new proposal.");
        }

        public async Task<Result> ValidateIncomingCardCapacityAsync(
            ProposalOperationDto operation,
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            if (!operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase))
                return Result.Success();
            var action = operation.ActionType.ToLowerInvariant();
            if (action is not ("create" or "move") ||
                !OperationParameterParser.TryGetRequiredGuid(parameters, "columnId", out var columnId, out _))
                return Result.Success();

            // CardService deliberately permits reordering within the current column even
            // when its WIP limit is full. Use the preceding moves/creates, not the stored column.
            if (action == "move" && TryGetOperationCardId(operation, parameters, out var cardId) &&
                await GetProjectedCardColumnAsync(cardId, cancellationToken) == columnId)
                return Result.Success();

            var column = await ReadColumnWithCardsAsync(columnId, cancellationToken);
            if (column is null || !WouldProjectedAddExceedWipLimit(column))
                return Result.Success();

            return Result.Failure(ErrorCodes.WipLimitExceeded, action == "create"
                ? $"Cannot add card, column '{column.Name}' has reached its WIP limit of {column.WipLimit}"
                : $"Cannot move card, target column '{column.Name}' has reached its WIP limit of {column.WipLimit}");
        }

        /// <summary>
        /// Folds one already-accepted operation into the projected column occupancy.
        /// Only operations that Apply turns into a change in a column's ACTIVE card count
        /// participate: card create, card move, active card delete, and the two lifecycle actions. The legacy
        /// <c>archive</c> verb keeps Block semantics and leaves the card active, so it
        /// contributes nothing. A delete or archive can free space for a following create/move.
        /// <see cref="ProposalHierarchyValidator"/> admits at most one hierarchy-affecting
        /// operation per proposal, so a delete or lifecycle operation cannot precede a restore.
        /// </summary>
        public async Task ProjectColumnOccupancyAsync(
            ProposalOperationDto operation,
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            if (!operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase))
                return;

            switch (operation.ActionType.ToLowerInvariant())
            {
                case "create":
                {
                    if (!OperationParameterParser.TryGetRequiredGuid(parameters, "columnId", out var createColumnId, out _))
                        return;
                    AddProjectedColumnDelta(createColumnId, 1);
                    if (Guid.TryParse(operation.TargetId, out var createdCardId))
                        _projectedCardColumns[createdCardId] = createColumnId;
                    return;
                }

                case "move":
                {
                    if (!TryGetOperationCardId(operation, parameters, out var moveCardId) ||
                        !OperationParameterParser.TryGetRequiredGuid(parameters, "columnId", out var targetColumnId, out _))
                        return;
                    var sourceColumnId = await GetProjectedCardColumnAsync(moveCardId, cancellationToken);
                    // Apply skips the WIP check for a same-column move, and so does this projection.
                    if (sourceColumnId == targetColumnId)
                        return;
                    // Capacity has already accepted this move. The source decrement is safe because
                    // ValidateCardArchiveStateAsync has already refused a move of an archived card,
                    // so the moved card is proven to be in the source column's ACTIVE count.
                    // Relaxing the one-lifecycle gate would let a restore precede a move of that
                    // same card, and this branch would then need the projected archive state, not
                    // just the projected column.
                    if (sourceColumnId.HasValue)
                        AddProjectedColumnDelta(sourceColumnId.Value, -1);
                    AddProjectedColumnDelta(targetColumnId, 1);
                    _projectedCardColumns[moveCardId] = targetColumnId;
                    return;
                }

                case "archive-lifecycle":
                case "restore-lifecycle":
                case "delete":
                {
                    if (!TryGetOperationCardId(operation, parameters, out var lifecycleCardId))
                        return;
                    var card = await ReadCardAsync(lifecycleCardId, cancellationToken);
                    if (card is null)
                        return;
                    if (operation.ActionType.Equals("delete", StringComparison.OrdinalIgnoreCase) && card.IsArchived)
                        return;
                    var lifecycleColumnId = _projectedCardColumns.TryGetValue(lifecycleCardId, out var plannedColumnId)
                        ? plannedColumnId
                        : card.ColumnId;
                    AddProjectedColumnDelta(
                        lifecycleColumnId,
                        operation.ActionType.Equals("restore-lifecycle", StringComparison.OrdinalIgnoreCase) ? 1 : -1);
                    return;
                }
            }
        }

        /// <summary>
        /// Whether one more active card would breach <paramref name="column"/>'s WIP limit on the
        /// board Apply will see at this point in the proposal. With no preceding occupancy change
        /// this is exactly <see cref="Taskdeck.Domain.Entities.Column.WouldExceedWipLimitIfAdded"/>;
        /// the delta is what stops an operation that takes the last slot first from letting a
        /// create, move or restore pass preview and then fail at execute with a full-proposal rollback.
        /// </summary>
        private bool WouldProjectedAddExceedWipLimit(Taskdeck.Domain.Entities.Column column)
        {
            if (!column.WipLimit.HasValue)
                return false;

            var projectedActiveCount = column.Cards.Count(card => !card.IsArchived) +
                                       _projectedColumnActiveDelta.GetValueOrDefault(column.Id);
            return projectedActiveCount >= column.WipLimit.Value;
        }

        private async Task<Taskdeck.Domain.Entities.Column?> ReadColumnWithCardsAsync(Guid columnId, CancellationToken cancellationToken)
        {
            if (!_columnsWithCards.TryGetValue(columnId, out var column))
            {
                column = await unitOfWork.Columns.GetByIdWithCardsAsync(columnId, cancellationToken);
                _columnsWithCards[columnId] = column;
            }
            return column;
        }

        private void AddProjectedColumnDelta(Guid columnId, int delta) =>
            _projectedColumnActiveDelta[columnId] = _projectedColumnActiveDelta.GetValueOrDefault(columnId) + delta;

        private async Task<Guid?> GetProjectedCardColumnAsync(Guid cardId, CancellationToken cancellationToken)
        {
            if (_projectedCardColumns.TryGetValue(cardId, out var plannedColumnId))
                return plannedColumnId;
            // A create with no targetId leaves Apply to generate the id, so a later move of that
            // card cannot be paired with its source column; leaving it unknown keeps the target
            // side of the delta and omits a source decrement we cannot attribute.
            if (_plannedCardIds.Contains(cardId))
                return null;
            return (await ReadCardAsync(cardId, cancellationToken))?.ColumnId;
        }

        private static bool TryGetOperationCardId(ProposalOperationDto operation, JsonElement parameters, out Guid cardId)
        {
            // Scope validation has already proven these agree when both are present.
            if (OperationParameterParser.TryGetRequiredGuid(parameters, "cardId", out cardId, out _))
                return true;
            return Guid.TryParse(operation.TargetId, out cardId);
        }

        public Result ValidateOperationAfterPlannedBoardArchive(ProposalOperationDto operation, JsonElement parameters)
        {
            if (!_isBoardArchivedInProposal || IsBoardUnarchiveOperation(operation, parameters))
                return Result.Success();

            return Result.Failure(
                ErrorCodes.InvalidOperation,
                "Cannot apply an operation after archiving the proposal board. Restore the board before making further changes.");
        }

        public void ApplyPlannedBoardArchiveState(ProposalOperationDto operation, JsonElement parameters)
        {
            if (!operation.TargetType.Equals("board", StringComparison.OrdinalIgnoreCase) ||
                !operation.ActionType.Equals("update", StringComparison.OrdinalIgnoreCase) ||
                !OperationParameterParser.TryGetOptionalBoolean(parameters, "isArchived", out var isArchivedProvided, out var isArchived, out _) ||
                !isArchivedProvided)
            {
                return;
            }

            _isBoardArchivedInProposal = isArchived;
        }

        private static bool IsBoardUnarchiveOperation(ProposalOperationDto operation, JsonElement parameters) =>
            operation.TargetType.Equals("board", StringComparison.OrdinalIgnoreCase) &&
            operation.ActionType.Equals("update", StringComparison.OrdinalIgnoreCase) &&
            OperationParameterParser.TryGetOptionalBoolean(parameters, "isArchived", out var isArchivedProvided, out var isArchived, out _) &&
            isArchivedProvided &&
            !isArchived;

        public async Task<Result> ValidateNewCardIdAsync(Guid cardId, CancellationToken cancellationToken)
        {
            // A create-card targetId of Guid.Empty parses successfully but the Card
            // aggregate rejects it at Apply. Reject it before preview so the approval
            // gate never registers an unusable planned card (#1319 preview == apply).
            if (cardId == Guid.Empty)
                return Result.Failure(ErrorCodes.ValidationError, "Create card id must be a non-empty identifier");

            if (_plannedCardIds.Contains(cardId))
                return Result.Failure(ErrorCodes.Conflict, "Create card id is duplicated within the proposal");

            if (!_cardBoardIds.TryGetValue(cardId, out var existingCardBoardId))
            {
                existingCardBoardId = (await ReadCardAsync(cardId, cancellationToken))?.BoardId;
                _cardBoardIds[cardId] = existingCardBoardId;
            }

            return existingCardBoardId.HasValue
                ? Result.Failure(ErrorCodes.Conflict, "Create card id already exists")
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
                cardBoardId = (await ReadCardAsync(cardId, cancellationToken))?.BoardId;
                _cardBoardIds[cardId] = cardBoardId;
            }

            return cardBoardId == BoardId
                ? Result.Success()
                : ScopeFailure("Operation card is outside the proposal board scope");
        }

        public async Task<Result> ValidateRelationEndpointsAsync(
            CardRelationEdge relation,
            long expectedRevision,
            bool remove,
            IBoardDependencyRepository dependencies,
            CancellationToken cancellationToken)
        {
            if (!BoardId.HasValue)
                return ScopeFailure("Operation card is outside the proposal board scope");

            var graph = await dependencies.GetAsync(BoardId.Value, cancellationToken) ?? new BoardDependencies(BoardId.Value);
            if (graph.Revision != expectedRevision)
                return Result.Failure(ErrorCodes.Conflict, "Relations changed. Reload before trying again.");

            var cards = (await unitOfWork.Cards.GetHierarchyByBoardIdAsync(BoardId.Value, cancellationToken))
                .ToDictionary(card => card.Id);
            var endpoints = cards.Values
                .Select(card => new CardRelationEndpoint(card.Id, card.BoardId, card.IsArchived))
                .ToList();
            foreach (var cardId in new[] { relation.SourceCardId, relation.TargetCardId }.Distinct())
            {
                if (_plannedCardIds.Contains(cardId))
                {
                    // A preceding create has a preallocated TargetId and will be active when this
                    // later relation operation stages inside the same executor transaction.
                    endpoints.Add(new CardRelationEndpoint(cardId, BoardId.Value, IsArchived: false));
                    continue;
                }

                var card = await ReadCardAsync(cardId, cancellationToken);
                if (card is null)
                    return Result.Failure(ErrorCodes.NotFound, "Card not found.");
                if (card.BoardId != BoardId.Value)
                    return ScopeFailure("Operation card is outside the proposal board scope");
                if (card.IsArchived)
                    return Result.Failure(ErrorCodes.InvalidOperation, "Card is archived. Restore it before editing relations.");
                if (!cards.ContainsKey(card.Id))
                    endpoints.Add(new CardRelationEndpoint(card.Id, card.BoardId, card.IsArchived));
            }

            try
            {
                // Match BoardRelationService.PrepareAsync: validate the requested revision,
                // then apply the requested add/remove to the complete current graph using all
                // board endpoints. Existing archived, non-target edges remain valid while the
                // requested endpoints must be active.
                CardRelationRules.Apply(BoardId.Value, graph.ReadRelations(), relation, remove, endpoints);
                return Result.Success();
            }
            catch (DomainException exception)
            {
                return Result.Failure(exception.ErrorCode, exception.Message);
            }
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

        public async Task<Result> ValidateAndRegisterNewColumnAsync(
            CreateColumnOperationParameters parameters,
            CancellationToken cancellationToken)
        {
            if (!BoardId.HasValue || parameters.BoardId != BoardId.Value)
                return ScopeFailure("Operation boardId is outside the proposal board scope");

            _boardColumns ??= (await unitOfWork.Columns.GetByBoardIdAsync(
                parameters.BoardId,
                cancellationToken)).ToList();

            var availabilityResult = ValidateCreateColumnPositionAvailability(_boardColumns, parameters);
            if (!availabilityResult.IsSuccess)
                return availabilityResult;

            if (_plannedColumnPositions.Contains(parameters.Position))
            {
                return Result.Failure(
                    ErrorCodes.Conflict,
                    $"Column position {parameters.Position} is duplicated within the proposal");
            }

            _plannedColumnPositions.Add(parameters.Position);
            return Result.Success();
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

internal sealed record CreateColumnOperationParameters(
    Guid BoardId,
    string Name,
    int Position,
    int? WipLimit);
