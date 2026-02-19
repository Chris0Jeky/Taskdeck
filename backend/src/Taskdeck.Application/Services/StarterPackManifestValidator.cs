using System.Text.Json;
using System.Text.RegularExpressions;
using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Services;

public sealed class StarterPackManifestValidator : IStarterPackManifestValidator
{
    private const string CurrentSchemaVersion = "1.0";
    private static readonly Regex SlugRegex = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex HexColorRegex = new("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant);
    private static readonly Regex SemVerRegex = new("^\\d+\\.\\d+\\.\\d+$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public StarterPackManifestValidationResult ValidateJson(string manifestJson)
    {
        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            return new StarterPackManifestValidationResult(
                null,
                new[] { new StarterPackManifestValidationError("$", "Manifest JSON cannot be empty.") });
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<StarterPackManifestDto>(manifestJson, SerializerOptions);
            if (manifest == null)
            {
                return new StarterPackManifestValidationResult(
                    null,
                    new[] { new StarterPackManifestValidationError("$", "Manifest JSON could not be parsed.") });
            }

            return Validate(manifest);
        }
        catch (JsonException ex)
        {
            return new StarterPackManifestValidationResult(
                null,
                new[] { new StarterPackManifestValidationError("$", $"Manifest JSON is invalid: {ex.Message}") });
        }
    }

    public StarterPackManifestValidationResult Validate(StarterPackManifestDto manifest)
    {
        var errors = new List<StarterPackManifestValidationError>();
        if (manifest == null)
        {
            errors.Add(new StarterPackManifestValidationError("$", "Manifest cannot be null."));
            return new StarterPackManifestValidationResult(null, errors);
        }

        var tags = NormalizeCollection(manifest.Tags, "$.tags", "Tags", errors);
        var compatibility = manifest.Compatibility;
        var requiredFeatures = compatibility == null
            ? []
            : NormalizeCollection(
                compatibility.RequiredFeatures,
                "$.compatibility.requiredFeatures",
                "Required features",
                errors);
        var labels = NormalizeCollection(manifest.Labels, "$.labels", "Labels", errors);
        var columns = NormalizeCollection(manifest.Columns, "$.columns", "Columns", errors);
        var templates = NormalizeCollection(manifest.Templates, "$.templates", "Templates", errors);
        var seedCards = NormalizeCollection(manifest.SeedCards, "$.seedCards", "Seed cards", errors);

        ValidateHeader(manifest, tags, errors);
        ValidateCompatibility(compatibility, requiredFeatures, errors);
        var knownLabelNames = ValidateLabels(labels, errors);
        var knownColumnNames = ValidateColumns(columns, errors);
        var knownTemplateIds = ValidateTemplates(templates, errors);
        ValidateSeedCards(seedCards, knownLabelNames, knownColumnNames, knownTemplateIds, errors);

        return new StarterPackManifestValidationResult(manifest, errors);
    }

    private static void ValidateHeader(
        StarterPackManifestDto manifest,
        List<string> tags,
        List<StarterPackManifestValidationError> errors)
    {
        if (!CurrentSchemaVersion.Equals(manifest.SchemaVersion?.Trim(), StringComparison.Ordinal))
        {
            errors.Add(new StarterPackManifestValidationError(
                "$.schemaVersion",
                $"Unsupported schema version '{manifest.SchemaVersion}'. Expected '{CurrentSchemaVersion}'."));
        }

        if (!IsSlug(manifest.PackId))
        {
            errors.Add(new StarterPackManifestValidationError(
                "$.packId",
                "Pack ID must be kebab-case (lowercase letters, numbers, and hyphens)."));
        }

        if (string.IsNullOrWhiteSpace(manifest.DisplayName))
        {
            errors.Add(new StarterPackManifestValidationError("$.displayName", "Display name is required."));
        }

        var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tags.Count; i++)
        {
            var tag = tags[i];
            if (string.IsNullOrWhiteSpace(tag))
            {
                errors.Add(new StarterPackManifestValidationError($"$.tags[{i}]", "Tag cannot be empty."));
                continue;
            }

            if (!IsSlug(tag))
            {
                errors.Add(new StarterPackManifestValidationError(
                    $"$.tags[{i}]",
                    "Tag must be kebab-case (lowercase letters, numbers, and hyphens)."));
            }

            if (!tagSet.Add(tag))
            {
                errors.Add(new StarterPackManifestValidationError($"$.tags[{i}]", $"Duplicate tag '{tag}'."));
            }
        }
    }

    private static void ValidateCompatibility(
        StarterPackCompatibilityDto? compatibility,
        List<string> requiredFeatures,
        List<StarterPackManifestValidationError> errors)
    {
        if (compatibility == null)
        {
            errors.Add(new StarterPackManifestValidationError("$.compatibility", "Compatibility section is required."));
            return;
        }

        if (!IsSemVer(compatibility.MinTaskdeckVersion))
        {
            errors.Add(new StarterPackManifestValidationError(
                "$.compatibility.minTaskdeckVersion",
                "Minimum Taskdeck version must use strict semver format (major.minor.patch)."));
        }

        if (!string.IsNullOrWhiteSpace(compatibility.MaxTaskdeckVersion) && !IsSemVer(compatibility.MaxTaskdeckVersion))
        {
            errors.Add(new StarterPackManifestValidationError(
                "$.compatibility.maxTaskdeckVersion",
                "Maximum Taskdeck version must use strict semver format (major.minor.patch)."));
        }

        if (TryParseSemVer(compatibility.MinTaskdeckVersion, out var minVersion) &&
            TryParseSemVer(compatibility.MaxTaskdeckVersion, out var maxVersion) &&
            CompareSemVer(minVersion, maxVersion) > 0)
        {
            errors.Add(new StarterPackManifestValidationError(
                "$.compatibility.maxTaskdeckVersion",
                "Maximum Taskdeck version must be greater than or equal to minimum version."));
        }

        var requiredFeatureSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < requiredFeatures.Count; i++)
        {
            var feature = requiredFeatures[i];
            if (string.IsNullOrWhiteSpace(feature))
            {
                errors.Add(new StarterPackManifestValidationError(
                    $"$.compatibility.requiredFeatures[{i}]",
                    "Required feature cannot be empty."));
                continue;
            }

            if (!IsSlug(feature))
            {
                errors.Add(new StarterPackManifestValidationError(
                    $"$.compatibility.requiredFeatures[{i}]",
                    "Required feature must be kebab-case."));
            }

            if (!requiredFeatureSet.Add(feature))
            {
                errors.Add(new StarterPackManifestValidationError(
                    $"$.compatibility.requiredFeatures[{i}]",
                    $"Duplicate required feature '{feature}'."));
            }
        }
    }

    private static HashSet<string> ValidateLabels(List<StarterPackLabelDto> labels, List<StarterPackManifestValidationError> errors)
    {
        var knownLabelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < labels.Count; i++)
        {
            var label = labels[i];
            if (label == null)
            {
                errors.Add(new StarterPackManifestValidationError($"$.labels[{i}]", "Label entry cannot be null."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(label.Name))
            {
                errors.Add(new StarterPackManifestValidationError($"$.labels[{i}].name", "Label name is required."));
                continue;
            }

            if (!knownLabelNames.Add(label.Name))
            {
                errors.Add(new StarterPackManifestValidationError(
                    $"$.labels[{i}].name",
                    $"Duplicate label name '{label.Name}'."));
            }

            if (!HexColorRegex.IsMatch(label.Color ?? string.Empty))
            {
                errors.Add(new StarterPackManifestValidationError(
                    $"$.labels[{i}].color",
                    "Label color must be a hex RGB value like '#3366CC'."));
            }
        }

        return knownLabelNames;
    }

    private static HashSet<string> ValidateColumns(List<StarterPackColumnDto> columns, List<StarterPackManifestValidationError> errors)
    {
        var knownColumnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var positions = new HashSet<int>();

        if (columns.Count == 0)
        {
            errors.Add(new StarterPackManifestValidationError("$.columns", "At least one column is required."));
            return knownColumnNames;
        }

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            if (column == null)
            {
                errors.Add(new StarterPackManifestValidationError($"$.columns[{i}]", "Column entry cannot be null."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(column.Name))
            {
                errors.Add(new StarterPackManifestValidationError($"$.columns[{i}].name", "Column name is required."));
                continue;
            }

            if (!knownColumnNames.Add(column.Name))
            {
                errors.Add(new StarterPackManifestValidationError(
                    $"$.columns[{i}].name",
                    $"Duplicate column name '{column.Name}'."));
            }

            if (column.Position < 0)
            {
                errors.Add(new StarterPackManifestValidationError(
                    $"$.columns[{i}].position",
                    "Column position cannot be negative."));
                continue;
            }

            if (!positions.Add(column.Position))
            {
                errors.Add(new StarterPackManifestValidationError(
                    $"$.columns[{i}].position",
                    $"Duplicate column position '{column.Position}'."));
            }

            if (column.WipLimit.HasValue && column.WipLimit.Value <= 0)
            {
                errors.Add(new StarterPackManifestValidationError(
                    $"$.columns[{i}].wipLimit",
                    "WIP limit must be greater than zero when provided."));
            }
        }

        if (positions.Count == columns.Count)
        {
            var expected = Enumerable.Range(0, columns.Count).ToArray();
            var actual = positions.OrderBy(value => value).ToArray();
            if (!expected.SequenceEqual(actual))
            {
                errors.Add(new StarterPackManifestValidationError(
                    "$.columns",
                    "Column positions must be contiguous and start at 0."));
            }
        }

        return knownColumnNames;
    }

    private static HashSet<string> ValidateTemplates(List<StarterPackCardTemplateDto> templates, List<StarterPackManifestValidationError> errors)
    {
        var knownTemplateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < templates.Count; i++)
        {
            var template = templates[i];
            if (template == null)
            {
                errors.Add(new StarterPackManifestValidationError($"$.templates[{i}]", "Template entry cannot be null."));
                continue;
            }

            if (!IsSlug(template.TemplateId))
            {
                errors.Add(new StarterPackManifestValidationError(
                    $"$.templates[{i}].templateId",
                    "Template ID must be kebab-case."));
            }
            else if (!knownTemplateIds.Add(template.TemplateId))
            {
                errors.Add(new StarterPackManifestValidationError(
                    $"$.templates[{i}].templateId",
                    $"Duplicate template ID '{template.TemplateId}'."));
            }

            if (string.IsNullOrWhiteSpace(template.Title))
            {
                errors.Add(new StarterPackManifestValidationError(
                    $"$.templates[{i}].title",
                    "Template title is required."));
            }

            var checklist = NormalizeCollection(
                template.Checklist,
                $"$.templates[{i}].checklist",
                "Template checklist",
                errors);
            for (var checklistIndex = 0; checklistIndex < checklist.Count; checklistIndex++)
            {
                if (string.IsNullOrWhiteSpace(checklist[checklistIndex]))
                {
                    errors.Add(new StarterPackManifestValidationError(
                        $"$.templates[{i}].checklist[{checklistIndex}]",
                        "Checklist item cannot be empty."));
                }
            }
        }

        return knownTemplateIds;
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

            var labelNames = NormalizeCollection(
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

    private static List<T> NormalizeCollection<T>(
        List<T>? collection,
        string path,
        string fieldName,
        List<StarterPackManifestValidationError> errors)
    {
        if (collection != null)
        {
            return collection;
        }

        errors.Add(new StarterPackManifestValidationError(path, $"{fieldName} must be an array."));
        return [];
    }

    private static bool IsSlug(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && SlugRegex.IsMatch(value);
    }

    private static bool IsSemVer(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && SemVerRegex.IsMatch(value);
    }

    private static bool TryParseSemVer(string? value, out (int Major, int Minor, int Patch) version)
    {
        version = default;
        if (!IsSemVer(value))
        {
            return false;
        }

        var parts = value!.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch))
        {
            return false;
        }

        version = (major, minor, patch);
        return true;
    }

    private static int CompareSemVer((int Major, int Minor, int Patch) left, (int Major, int Minor, int Patch) right)
    {
        var major = left.Major.CompareTo(right.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = left.Minor.CompareTo(right.Minor);
        if (minor != 0)
        {
            return minor;
        }

        return left.Patch.CompareTo(right.Patch);
    }
}

