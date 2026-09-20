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
public static partial class ProposalOperationContractValidator
{
    public static async Task<Result> ValidateAsync(
        IUnitOfWork unitOfWork,
        Guid? proposalBoardId,
        IEnumerable<ProposalOperationDto> operations,
        CancellationToken cancellationToken = default,
        IBoardDependencyRepository? dependencies = null)
    {
        var materializedOperations = operations.ToList();
        var shape = ValidateShape(materializedOperations, out _);
        if (!shape.IsSuccess) return shape;
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
            }
            if (!OperationParameterParser.TryDeserializeParameters(operation.Parameters, out var parameters, out var parseError))
                return Result.Failure(ErrorCodes.ValidationError, parseError);

            var labelAction = CardLabelOperationVocabulary.Classify(operation.ActionType);

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

    private static async Task<Result> ValidateOperationFieldsAsync(
        BoardValidationContext validationContext,
        ProposalOperationDto operation,
        JsonElement parameters,
        CardLabelOperationAction labelAction,
        CancellationToken cancellationToken)
    {
        // Shape and parameter support have already passed ValidateShape. This half
        // resolves only facts that depend on the proposal's board and stored state.
        if (operation.TargetType.Equals("column", StringComparison.OrdinalIgnoreCase) &&
            operation.ActionType.Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            var column = ParseCreateColumnParameters(operation, parameters);
            return await validationContext.ValidateAndRegisterNewColumnAsync(column.Value, cancellationToken);
        }
        if (!operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase))
            return Result.Success();

        if (operation.ActionType.ToLowerInvariant() is "create" or "update")
        {
            OperationParameterParser.TryGetEstimatedEffortMinutes(parameters, out var estimate, out _);
            OperationParameterParser.TryGetOptionalBoolean(parameters, "clearEstimatedEffort", out _, out var clearEstimate, out _);
            if (operation.ActionType.Equals("update", StringComparison.OrdinalIgnoreCase) && (estimate.HasValue || clearEstimate))
            {
                var version = await validationContext.ValidateEstimateVersionAsync(parameters, cancellationToken);
                if (!version.IsSuccess) return version;
            }
            return await ValidateLabelsAsync(validationContext, parameters, cancellationToken);
        }

        if (labelAction is not (CardLabelOperationAction.Add or CardLabelOperationAction.Remove))
            return Result.Success();
        if (!validationContext.BoardId.HasValue)
            return ScopeFailure("Label operation requires a proposal board scope");
        if (parameters.TryGetProperty("labelId", out var labelId))
            return await validationContext.ContainsLabelIdAsync(labelId.GetGuid(), cancellationToken)
                ? Result.Success() : Result.Failure(ErrorCodes.NotFound, "Label was not found on the proposal board");
        var name = parameters.GetProperty("labelName").GetString()!;
        var count = await validationContext.GetLabelNameMatchCountAsync(name, cancellationToken);
        return count == 0 ? Result.Failure(ErrorCodes.NotFound, "Label was not found on the proposal board")
            : count > 1 ? AmbiguousLabelFailure(name) : Result.Success();
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
            var pinned = RequiresExclusiveCardWrite(operation, parameters);
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
