using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

/// <summary>
/// Checks seed cards for idempotency: detects cards that already exist on
/// the board, duplicates within the manifest, and unresolvable column/label
/// references. Produces the final list of seed cards that should be created.
/// </summary>
public sealed class StarterPackIdempotencyChecker
{
    /// <summary>
    /// Seed-card-level record used during apply planning.
    /// </summary>
    public sealed record PlannedSeedCard(
        StarterPackSeedCardDto SeedCard,
        string ColumnName,
        List<string> LabelNames);

    /// <summary>
    /// Evaluates each seed card in the manifest against the board state and
    /// the conflict report to determine which cards can be created.
    /// Appends actions and conflicts to the provided lists.
    /// </summary>
    public List<PlannedSeedCard> Check(
        Board board,
        StarterPackManifestDto manifest,
        StarterPackConflictReport conflictReport)
    {
        var actions = conflictReport.Actions;
        var conflicts = conflictReport.Conflicts;
        var existingColumnsByName = conflictReport.ExistingColumnsByName;
        var resolvableColumnNames = conflictReport.ResolvableColumnNames;
        var resolvableLabelNames = conflictReport.ResolvableLabelNames;

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
                    seedCard.ColumnName,
                    StarterPackConflictSeverity.Warning));
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
                        labelName,
                        StarterPackConflictSeverity.Warning));
                    hasConflict = true;
                }
            }

            if (hasConflict)
            {
                actions.Add(new StarterPackApplyActionDto(
                    "seedCard",
                    "skip",
                    $"{seedCard.Title} @ {seedCard.ColumnName}",
                    "Seed card references unresolved column or label metadata."));
                continue;
            }

            if (existingColumnsByName.TryGetValue(seedCard.ColumnName, out var existingColumn) &&
                board.Cards.Any(card =>
                    card.ColumnId == existingColumn.Id &&
                    string.Equals(card.Title, seedCard.Title, StringComparison.OrdinalIgnoreCase)))
            {
                conflicts.Add(new StarterPackApplyConflictDto(
                    "SeedCardAlreadyExistsConflict",
                    $"$.seedCards[{index}]",
                    $"Seed card '{seedCard.Title}' already exists in column '{seedCard.ColumnName}' and will be skipped.",
                    $"{seedCard.Title} @ {seedCard.ColumnName}",
                    null,
                    StarterPackConflictSeverity.Warning));
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
                conflicts.Add(new StarterPackApplyConflictDto(
                    "SeedCardDuplicateInManifestConflict",
                    $"$.seedCards[{index}]",
                    $"Seed card '{seedCard.Title}' is duplicated in column '{seedCard.ColumnName}' and will be skipped.",
                    $"{seedCard.Title} @ {seedCard.ColumnName}",
                    $"{seedCard.Title} @ {seedCard.ColumnName}",
                    StarterPackConflictSeverity.Warning));
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

        return plannedSeedCards;
    }
}
