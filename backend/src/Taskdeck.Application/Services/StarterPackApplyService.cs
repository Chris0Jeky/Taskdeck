using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class StarterPackApplyService : IStarterPackApplyService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStarterPackManifestValidator _manifestValidator;

    public StarterPackApplyService(
        IUnitOfWork unitOfWork,
        IStarterPackManifestValidator manifestValidator)
    {
        _unitOfWork = unitOfWork;
        _manifestValidator = manifestValidator;
    }

    public async Task<Result<StarterPackApplyResultDto>> ApplyToBoardAsync(
        Guid boardId,
        ApplyStarterPackDto dto,
        CancellationToken cancellationToken = default)
    {
        if (boardId == Guid.Empty)
        {
            return Result.Failure<StarterPackApplyResultDto>(
                ErrorCodes.ValidationError,
                "Board ID cannot be empty.");
        }

        if (dto.Manifest == null)
        {
            return Result.Failure<StarterPackApplyResultDto>(
                ErrorCodes.ValidationError,
                "Manifest is required.");
        }

        var board = await _unitOfWork.Boards.GetByIdWithDetailsAsync(boardId, cancellationToken);
        if (board == null)
        {
            return Result.Failure<StarterPackApplyResultDto>(
                ErrorCodes.NotFound,
                $"Board with ID {boardId} not found.");
        }

        if (board.IsArchived)
        {
            return Result.Failure<StarterPackApplyResultDto>(
                ErrorCodes.InvalidOperation,
                "Cannot apply a starter pack to an archived board.");
        }

        var validationResult = _manifestValidator.Validate(dto.Manifest);
        if (!validationResult.IsValid)
        {
            return Result.Failure<StarterPackApplyResultDto>(
                ErrorCodes.ValidationError,
                BuildValidationSummary(validationResult.Errors));
        }

        var manifest = validationResult.Manifest!;

        var actions = new List<StarterPackApplyActionDto>();
        var conflicts = new List<StarterPackApplyConflictDto>();

        var existingLabelsByName = board.Labels
            .ToDictionary(label => label.Name, StringComparer.OrdinalIgnoreCase);
        var existingColumnsByName = board.Columns
            .ToDictionary(column => column.Name, StringComparer.OrdinalIgnoreCase);
        var existingColumnsByPosition = board.Columns
            .ToDictionary(column => column.Position, column => column);

        var plannedLabels = new List<StarterPackLabelDto>();
        var plannedLabelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var plannedColumns = new List<StarterPackColumnDto>();
        var plannedColumnsByName = new Dictionary<string, StarterPackColumnDto>(StringComparer.OrdinalIgnoreCase);
        var plannedColumnsByPosition = new Dictionary<int, StarterPackColumnDto>();

        for (var index = 0; index < manifest.Labels.Count; index++)
        {
            var label = manifest.Labels[index];

            if (existingLabelsByName.TryGetValue(label.Name, out var existingLabel))
            {
                if (string.Equals(existingLabel.ColorHex, label.Color, StringComparison.OrdinalIgnoreCase))
                {
                    actions.Add(new StarterPackApplyActionDto(
                        "label",
                        "skip",
                        label.Name,
                        "Label already exists with the same color."));
                }
                else
                {
                    conflicts.Add(new StarterPackApplyConflictDto(
                        "LabelColorConflict",
                        $"$.labels[{index}].color",
                        $"Label '{label.Name}' already exists with a different color.",
                        existingLabel.ColorHex,
                        label.Color));
                }

                continue;
            }

            plannedLabels.Add(label);
            plannedLabelNames.Add(label.Name);
            actions.Add(new StarterPackApplyActionDto(
                "label",
                "create",
                label.Name,
                "Label will be created."));
        }

        for (var index = 0; index < manifest.Columns.Count; index++)
        {
            var column = manifest.Columns[index];

            if (existingColumnsByName.TryGetValue(column.Name, out var existingColumn))
            {
                if (existingColumn.Position == column.Position &&
                    Nullable.Equals(existingColumn.WipLimit, column.WipLimit))
                {
                    actions.Add(new StarterPackApplyActionDto(
                        "column",
                        "skip",
                        column.Name,
                        "Column already exists with the same definition."));
                }
                else
                {
                    conflicts.Add(new StarterPackApplyConflictDto(
                        "ColumnDefinitionConflict",
                        $"$.columns[{index}]",
                        $"Column '{column.Name}' already exists with a different definition.",
                        DescribeColumn(existingColumn.Position, existingColumn.WipLimit),
                        DescribeColumn(column.Position, column.WipLimit)));
                }

                continue;
            }

            if (existingColumnsByPosition.TryGetValue(column.Position, out var occupyingColumn))
            {
                conflicts.Add(new StarterPackApplyConflictDto(
                    "ColumnPositionConflict",
                    $"$.columns[{index}].position",
                    $"Column position '{column.Position}' is already occupied by '{occupyingColumn.Name}'.",
                    occupyingColumn.Name,
                    column.Name));
                continue;
            }

            if (plannedColumnsByPosition.TryGetValue(column.Position, out var plannedOccupyingColumn))
            {
                conflicts.Add(new StarterPackApplyConflictDto(
                    "ColumnPositionConflict",
                    $"$.columns[{index}].position",
                    $"Column position '{column.Position}' is already reserved by '{plannedOccupyingColumn.Name}'.",
                    plannedOccupyingColumn.Name,
                    column.Name));
                continue;
            }

            plannedColumns.Add(column);
            plannedColumnsByName[column.Name] = column;
            plannedColumnsByPosition[column.Position] = column;
            actions.Add(new StarterPackApplyActionDto(
                "column",
                "create",
                column.Name,
                "Column will be created."));
        }

        var resolvableColumnNames = new HashSet<string>(existingColumnsByName.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var columnName in plannedColumnsByName.Keys)
        {
            resolvableColumnNames.Add(columnName);
        }

        var resolvableLabelNames = new HashSet<string>(existingLabelsByName.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var labelName in plannedLabelNames)
        {
            resolvableLabelNames.Add(labelName);
        }

        var plannedSeedCards = new List<PlannedSeedCard>();
        for (var index = 0; index < manifest.SeedCards.Count; index++)
        {
            var seedCard = manifest.SeedCards[index];
            var hasConflict = false;

            if (!resolvableColumnNames.Contains(seedCard.ColumnName))
            {
                conflicts.Add(new StarterPackApplyConflictDto(
                    "SeedCardColumnConflict",
                    $"$.seedCards[{index}].columnName",
                    $"Seed card '{seedCard.Title}' references column '{seedCard.ColumnName}' that cannot be resolved.",
                    null,
                    seedCard.ColumnName));
                hasConflict = true;
            }

            var deduplicatedLabelNames = new List<string>();
            var seenLabelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var labelIndex = 0; labelIndex < seedCard.Labels.Count; labelIndex++)
            {
                var labelName = seedCard.Labels[labelIndex];
                if (!seenLabelNames.Add(labelName))
                {
                    continue;
                }

                deduplicatedLabelNames.Add(labelName);
                if (!resolvableLabelNames.Contains(labelName))
                {
                    conflicts.Add(new StarterPackApplyConflictDto(
                        "SeedCardLabelConflict",
                        $"$.seedCards[{index}].labels[{labelIndex}]",
                        $"Seed card '{seedCard.Title}' references label '{labelName}' that cannot be resolved.",
                        null,
                        labelName));
                    hasConflict = true;
                }
            }

            if (hasConflict)
            {
                continue;
            }

            if (existingColumnsByName.TryGetValue(seedCard.ColumnName, out var existingColumn) &&
                board.Cards.Any(card =>
                    card.ColumnId == existingColumn.Id &&
                    string.Equals(card.Title, seedCard.Title, StringComparison.OrdinalIgnoreCase)))
            {
                actions.Add(new StarterPackApplyActionDto(
                    "seedCard",
                    "skip",
                    $"{seedCard.Title} @ {seedCard.ColumnName}",
                    "Seed card already exists in the target column."));
                continue;
            }

            if (plannedSeedCards.Any(candidate =>
                string.Equals(candidate.ColumnName, seedCard.ColumnName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.SeedCard.Title, seedCard.Title, StringComparison.OrdinalIgnoreCase)))
            {
                actions.Add(new StarterPackApplyActionDto(
                    "seedCard",
                    "skip",
                    $"{seedCard.Title} @ {seedCard.ColumnName}",
                    "Duplicate seed card in manifest apply plan."));
                continue;
            }

            plannedSeedCards.Add(new PlannedSeedCard(seedCard, seedCard.ColumnName, deduplicatedLabelNames));
            actions.Add(new StarterPackApplyActionDto(
                "seedCard",
                "create",
                $"{seedCard.Title} @ {seedCard.ColumnName}",
                "Seed card will be created."));
        }

        var preview = new StarterPackApplyResultDto(
            board.Id,
            manifest.PackId,
            dto.DryRun,
            false,
            actions,
            conflicts);

        if (dto.DryRun || conflicts.Count > 0)
        {
            return Result.Success(preview);
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var labelsByName = new Dictionary<string, Label>(StringComparer.OrdinalIgnoreCase);
            foreach (var label in board.Labels)
            {
                labelsByName[label.Name] = label;
            }

            foreach (var plannedLabel in plannedLabels)
            {
                var label = new Label(board.Id, plannedLabel.Name, plannedLabel.Color);
                await _unitOfWork.Labels.AddAsync(label, cancellationToken);
                labelsByName[label.Name] = label;
            }

            var columnsByName = new Dictionary<string, Column>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in board.Columns)
            {
                columnsByName[column.Name] = column;
            }

            foreach (var plannedColumn in plannedColumns)
            {
                var column = new Column(board.Id, plannedColumn.Name, plannedColumn.Position, plannedColumn.WipLimit);
                await _unitOfWork.Columns.AddAsync(column, cancellationToken);
                columnsByName[column.Name] = column;
            }

            var nextPositionByColumnId = board.Columns.ToDictionary(
                column => column.Id,
                column => column.Cards.Any() ? column.Cards.Max(card => card.Position) + 1 : 0);

            foreach (var plannedColumn in plannedColumns)
            {
                if (columnsByName.TryGetValue(plannedColumn.Name, out var column))
                {
                    nextPositionByColumnId[column.Id] = 0;
                }
            }

            foreach (var plannedSeedCard in plannedSeedCards)
            {
                if (!columnsByName.TryGetValue(plannedSeedCard.ColumnName, out var column))
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Result.Failure<StarterPackApplyResultDto>(
                        ErrorCodes.UnexpectedError,
                        $"Resolved column '{plannedSeedCard.ColumnName}' was not available during apply.");
                }

                if (!nextPositionByColumnId.TryGetValue(column.Id, out var nextPosition))
                {
                    nextPosition = 0;
                }

                var card = new Card(
                    board.Id,
                    column.Id,
                    plannedSeedCard.SeedCard.Title,
                    plannedSeedCard.SeedCard.Description,
                    null,
                    nextPosition);

                foreach (var labelName in plannedSeedCard.LabelNames)
                {
                    if (!labelsByName.TryGetValue(labelName, out var label))
                    {
                        await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                        return Result.Failure<StarterPackApplyResultDto>(
                            ErrorCodes.UnexpectedError,
                            $"Resolved label '{labelName}' was not available during apply.");
                    }

                    card.AddLabel(new CardLabel(card.Id, label.Id));
                }

                await _unitOfWork.Cards.AddAsync(card, cancellationToken);
                nextPositionByColumnId[column.Id] = nextPosition + 1;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var appliedResult = preview with { Applied = true };
            return Result.Success(appliedResult);
        }
        catch (DomainException ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<StarterPackApplyResultDto>(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<StarterPackApplyResultDto>(
                ErrorCodes.UnexpectedError,
                $"Starter pack apply failed: {ex.Message}");
        }
    }

    private static string BuildValidationSummary(IReadOnlyList<StarterPackManifestValidationError> errors)
    {
        if (errors.Count == 0)
        {
            return "Manifest validation failed.";
        }

        var topErrors = errors
            .Take(5)
            .Select(error => $"{error.Path}: {error.Message}");
        var suffix = errors.Count > 5
            ? $" (+{errors.Count - 5} more)"
            : string.Empty;

        return $"Manifest validation failed: {string.Join("; ", topErrors)}{suffix}";
    }

    private static string DescribeColumn(int position, int? wipLimit)
    {
        return $"position={position}, wipLimit={(wipLimit.HasValue ? wipLimit.Value.ToString() : "null")}";
    }

    private sealed record PlannedSeedCard(
        StarterPackSeedCardDto SeedCard,
        string ColumnName,
        List<string> LabelNames);
}
