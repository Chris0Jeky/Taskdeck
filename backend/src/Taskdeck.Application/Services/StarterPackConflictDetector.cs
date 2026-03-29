using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

/// <summary>
/// Detects conflicts between a starter-pack manifest and the current state
/// of a board: duplicate existing names/positions, color mismatches, definition
/// mismatches, and position collisions.
/// </summary>
public sealed class StarterPackConflictDetector
{
    /// <summary>
    /// Analyses the board against the manifest and produces planned create/skip
    /// actions plus any conflicts for labels and columns.
    /// </summary>
    public StarterPackConflictReport DetectConflicts(
        Board board,
        StarterPackManifestDto manifest)
    {
        var actions = new List<StarterPackApplyActionDto>();
        var conflicts = new List<StarterPackApplyConflictDto>();

        var referencedLabelNames = manifest.Labels
            .Select(label => label.Name)
            .Concat(manifest.SeedCards.SelectMany(seedCard => seedCard.Labels))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingLabelGroupsByName = board.Labels
            .GroupBy(label => label.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var duplicateLabelGroup in existingLabelGroupsByName.Where(group =>
                     group.Count() > 1 && referencedLabelNames.Contains(group.Key)))
        {
            var existingColors = string.Join(", ",
                duplicateLabelGroup
                    .Select(label => label.ColorHex)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(color => color, StringComparer.OrdinalIgnoreCase));

            conflicts.Add(new StarterPackApplyConflictDto(
                "ExistingLabelNameConflict",
                "$.board.labels",
                $"Board already contains duplicate label name '{duplicateLabelGroup.Key}'. Resolve duplicate names before applying starter packs.",
                existingColors,
                duplicateLabelGroup.Key));
        }

        var existingLabelsByName = existingLabelGroupsByName
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var referencedColumnNames = manifest.Columns
            .Select(column => column.Name)
            .Concat(manifest.SeedCards.Select(seedCard => seedCard.ColumnName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referencedColumnPositions = manifest.Columns
            .Select(column => column.Position)
            .ToHashSet();

        var existingColumnGroupsByName = board.Columns
            .GroupBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var duplicateColumnNameGroup in existingColumnGroupsByName.Where(group =>
                     group.Count() > 1 && referencedColumnNames.Contains(group.Key)))
        {
            var existingDefinitions = string.Join("; ",
                duplicateColumnNameGroup
                    .Select(column => DescribeColumn(column.Position, column.WipLimit))
                    .OrderBy(definition => definition, StringComparer.Ordinal));

            conflicts.Add(new StarterPackApplyConflictDto(
                "ExistingColumnNameConflict",
                "$.board.columns",
                $"Board already contains duplicate column name '{duplicateColumnNameGroup.Key}'. Resolve duplicate names before applying starter packs.",
                existingDefinitions,
                duplicateColumnNameGroup.Key));
        }

        var existingColumnsByName = existingColumnGroupsByName
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var existingColumnGroupsByPosition = board.Columns
            .GroupBy(column => column.Position)
            .ToList();
        foreach (var duplicateColumnPositionGroup in existingColumnGroupsByPosition.Where(group =>
                     group.Count() > 1 && referencedColumnPositions.Contains(group.Key)))
        {
            var existingNames = string.Join(", ",
                duplicateColumnPositionGroup
                    .Select(column => column.Name)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

            conflicts.Add(new StarterPackApplyConflictDto(
                "ExistingColumnPositionConflict",
                "$.board.columns",
                $"Board already contains multiple columns at position '{duplicateColumnPositionGroup.Key}'. Resolve duplicate positions before applying starter packs.",
                existingNames,
                duplicateColumnPositionGroup.Key.ToString()));
        }

        var existingColumnsByPosition = existingColumnGroupsByPosition
            .ToDictionary(group => group.Key, group => group.First());

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

        return new StarterPackConflictReport(
            actions,
            conflicts,
            plannedLabels,
            plannedLabelNames,
            plannedColumns,
            plannedColumnsByName,
            existingLabelsByName,
            existingColumnsByName,
            resolvableColumnNames,
            resolvableLabelNames);
    }

    internal static string DescribeColumn(int position, int? wipLimit)
    {
        return $"position={position}, wipLimit={(wipLimit.HasValue ? wipLimit.Value.ToString() : "null")}";
    }
}

/// <summary>
/// Intermediate output from conflict detection, carrying the plan and resolved
/// name sets needed by the idempotency checker and the apply phase.
/// </summary>
public sealed record StarterPackConflictReport(
    List<StarterPackApplyActionDto> Actions,
    List<StarterPackApplyConflictDto> Conflicts,
    List<StarterPackLabelDto> PlannedLabels,
    HashSet<string> PlannedLabelNames,
    List<StarterPackColumnDto> PlannedColumns,
    Dictionary<string, StarterPackColumnDto> PlannedColumnsByName,
    Dictionary<string, Label> ExistingLabelsByName,
    Dictionary<string, Column> ExistingColumnsByName,
    HashSet<string> ResolvableColumnNames,
    HashSet<string> ResolvableLabelNames);
