using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Services;

/// <summary>
/// Validates cross-entity references in a starter-pack manifest: seed cards
/// referencing columns, labels, and templates that were declared earlier in
/// the same manifest.
/// Depends on the known-name sets produced by <see cref="StarterPackSchemaValidator"/>.
/// </summary>
public sealed class StarterPackSemanticValidator
{
    public void Validate(
        StarterPackSchemaValidationOutput schemaOutput,
        List<StarterPackManifestValidationError> errors)
    {
        ValidateSeedCards(
            schemaOutput.NormalizedSeedCards,
            schemaOutput.KnownLabelNames,
            schemaOutput.KnownColumnNames,
            schemaOutput.KnownTemplateIds,
            errors);
    }

    private static void ValidateSeedCards(
        List<StarterPackSeedCardDto> seedCards,
        HashSet<string> knownLabelNames,
        HashSet<string> knownColumnNames,
        HashSet<string> knownTemplateIds,
        List<StarterPackManifestValidationError> errors)
    {
        for (var i = 0; i < seedCards.Count; i++)
        {
            var seedCard = seedCards[i];
            if (seedCard == null)
            {
                errors.Add(new StarterPackManifestValidationError($"$.seedCards[{i}]", "Seed card entry cannot be null."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(seedCard.Title))
            {
                errors.Add(new StarterPackManifestValidationError($"$.seedCards[{i}].title", "Seed card title is required."));
            }

            if (string.IsNullOrWhiteSpace(seedCard.ColumnName))
            {
                errors.Add(new StarterPackManifestValidationError(
                    $"$.seedCards[{i}].columnName",
                    "Seed card columnName is required."));
            }
            else if (!knownColumnNames.Contains(seedCard.ColumnName))
            {
                errors.Add(new StarterPackManifestValidationError(
                    $"$.seedCards[{i}].columnName",
                    $"Seed card references unknown column '{seedCard.ColumnName}'."));
            }

            if (!string.IsNullOrWhiteSpace(seedCard.TemplateId) && !knownTemplateIds.Contains(seedCard.TemplateId))
            {
                errors.Add(new StarterPackManifestValidationError(
                    $"$.seedCards[{i}].templateId",
                    $"Seed card references unknown template '{seedCard.TemplateId}'."));
            }

            var labelNames = StarterPackSchemaValidator.NormalizeCollection(
                seedCard.Labels,
                $"$.seedCards[{i}].labels",
                "Seed card labels",
                errors);
            for (var labelIndex = 0; labelIndex < labelNames.Count; labelIndex++)
            {
                var labelName = labelNames[labelIndex];
                if (string.IsNullOrWhiteSpace(labelName))
                {
                    errors.Add(new StarterPackManifestValidationError(
                        $"$.seedCards[{i}].labels[{labelIndex}]",
                        "Seed card label cannot be empty."));
                    continue;
                }

                if (!knownLabelNames.Contains(labelName))
                {
                    errors.Add(new StarterPackManifestValidationError(
                        $"$.seedCards[{i}].labels[{labelIndex}]",
                        $"Seed card references unknown label '{labelName}'."));
                }
            }
        }
    }
}
