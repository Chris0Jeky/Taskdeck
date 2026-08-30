using System.Text.RegularExpressions;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Processing;

namespace Taskdeck.Application.Processing;

public sealed record ProcessorManifestValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Enforces the manifest rules of <c>processor-manifest.v1.schema.json</c> in code (shape, bounds,
/// enumerations — unknown members are already rejected at parse time) plus the semantic rules a JSON
/// schema cannot express (capability vocabulary, locality/network consistency). The schema file is
/// the published contract; it is not evaluated at runtime. A manifest that fails here is rejected at
/// registration (CF-04 conformance rule 1) so the router never has to reason about a processor whose
/// declarations are inconsistent.
/// </summary>
public static class ProcessorManifestValidator
{
    public const int MaxIdLength = 120;
    public const int MaxVersionLength = 40;
    public const int MaxDisplayNameLength = 120;
    public const int MaxAcceptLength = 100;
    public const int MaxLanguageLength = 20;
    public const int MaxFeatureLength = 80;
    public const int MaxHostLength = 255;
    public const int MaxOutputSchemaLength = 200;

    private static readonly Regex IdPattern = new("^[a-z0-9]+(?:[._-][a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex CurrencyPattern = new("^[A-Z]{3}$", RegexOptions.Compiled);
    private static readonly HashSet<string> DataClasses = new(StringComparer.Ordinal)
    {
        "text", "audio", "image", "document", "metadata"
    };

    public static ProcessorManifestValidationResult Validate(ProcessorManifest? manifest)
    {
        var errors = new List<string>();

        if (manifest is null)
        {
            errors.Add("manifest: missing");
            return new ProcessorManifestValidationResult(errors);
        }

        if (string.IsNullOrWhiteSpace(manifest.Id) || manifest.Id.Length > MaxIdLength || !IdPattern.IsMatch(manifest.Id))
            errors.Add($"id: required, at most {MaxIdLength} chars, matching {IdPattern}");

        if (string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Length > MaxVersionLength)
            errors.Add($"version: required, at most {MaxVersionLength} chars");

        if (manifest.DisplayName is { Length: > MaxDisplayNameLength })
            errors.Add($"displayName: at most {MaxDisplayNameLength} chars");

        ValidateCapabilities(manifest.Capabilities, errors);

        if (manifest.Execution is null || !Enum.IsDefined(manifest.Execution.Value))
            errors.Add("execution: required, one of in-process | sidecar | remote");

        if (manifest.Locality is null || !Enum.IsDefined(manifest.Locality.Value))
            errors.Add("locality: required, one of local | remote | hybrid");

        ValidateStringList(manifest.Accepts, "accepts", MaxAcceptLength, required: true, unique: false, errors);
        ValidateStringList(manifest.Languages, "languages", MaxLanguageLength, required: false, unique: false, errors);
        ValidateStringList(manifest.Features, "features", MaxFeatureLength, required: false, unique: true, errors);
        ValidateStringList(manifest.OutputSchemas, "outputSchemas", MaxOutputSchemaLength, required: false, unique: false, errors);

        ValidateResources(manifest.Resources, errors);
        ValidatePrivacy(manifest, errors);
        ValidateCostModel(manifest.CostModel, errors);

        return new ProcessorManifestValidationResult(errors);
    }

    private static void ValidateCapabilities(IReadOnlyList<string>? capabilities, List<string> errors)
    {
        if (capabilities is null || capabilities.Count == 0)
        {
            errors.Add("capabilities: at least one capability is required");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var reportedUnknown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in capabilities)
        {
            var value = capability ?? string.Empty;
            if (!seen.Add(value))
            {
                errors.Add($"capabilities: '{value}' is declared twice");
                continue;
            }

            if (!ProcessingCapability.IsKnown(value) && reportedUnknown.Add(value))
                errors.Add($"capabilities: '{value}' is not a known capability");
        }
    }

    private static void ValidateStringList(
        IReadOnlyList<string>? values,
        string field,
        int maxLength,
        bool required,
        bool unique,
        List<string> errors)
    {
        if (values is null)
        {
            if (required)
                errors.Add($"{field}: at least one value is required");
            return;
        }

        if (required && values.Count == 0)
        {
            errors.Add($"{field}: at least one value is required");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            {
                errors.Add($"{field}: values must be non-empty and at most {maxLength} chars");
                continue;
            }

            if (unique && !seen.Add(value))
                errors.Add($"{field}: '{value}' is declared twice");
        }
    }

    private static void ValidateResources(ProcessorResourceRequirements? resources, List<string> errors)
    {
        if (resources is null)
        {
            errors.Add("resources: required");
            return;
        }

        if (resources.Cpu is null)
            errors.Add("resources.cpu: required");

        if (resources.Gpu is null || !Enum.IsDefined(resources.Gpu.Value))
            errors.Add("resources.gpu: required, one of none | optional | required");

        if (resources.MinVramMb is < 0)
            errors.Add("resources.minVramMb: cannot be negative");

        if (resources.EstimatedRamMb is < 0)
            errors.Add("resources.estimatedRamMb: cannot be negative");

        if (resources.Gpu == ProcessorGpuRequirement.None && resources.MinVramMb is > 0)
            errors.Add("resources.minVramMb: a processor that declares no GPU cannot require VRAM");
    }

    private static void ValidatePrivacy(ProcessorManifest manifest, List<string> errors)
    {
        var privacy = manifest.Privacy;
        if (privacy is null)
        {
            errors.Add("privacy: required");
            return;
        }

        ValidateStringList(privacy.AllowedHosts, "privacy.allowedHosts", MaxHostLength, required: false, unique: true, errors);

        if (privacy.DataClasses is not null)
        {
            foreach (var dataClass in privacy.DataClasses)
            {
                if (!DataClasses.Contains(dataClass))
                    errors.Add($"privacy.dataClasses: '{dataClass}' is not one of text | audio | image | document | metadata");
            }
        }

        if (privacy.NetworkRequired is null)
        {
            // Report the field-level problems above first so a registry sees every error at once,
            // then stop: the consistency rules below are meaningless without the network flag.
            errors.Add("privacy.networkRequired: required");
            return;
        }

        var networkRequired = privacy.NetworkRequired.Value;

        if (!networkRequired && privacy.AllowedHosts is { Count: > 0 })
            errors.Add("privacy: a processor with networkRequired=false cannot declare allowedHosts");

        if (manifest.Execution == ProcessorExecutionMode.Remote && !networkRequired)
            errors.Add("privacy: a remote processor must declare networkRequired=true");

        if (manifest.Locality == ProcessorLocality.Local && networkRequired)
            errors.Add("privacy: a local processor cannot declare networkRequired=true (use hybrid or remote)");

        if (manifest.Locality == ProcessorLocality.Local && manifest.Execution == ProcessorExecutionMode.Remote)
            errors.Add("locality: a local processor cannot execute remotely");

        if (networkRequired && (privacy.AllowedHosts is null || privacy.AllowedHosts.Count == 0))
            errors.Add("privacy.allowedHosts: a processor that requires the network must declare its hosts");
    }

    private static void ValidateCostModel(ProcessorCostModel? costModel, List<string> errors)
    {
        if (costModel is null)
            return;

        if (costModel.Type is null || !Enum.IsDefined(costModel.Type.Value))
            errors.Add("costModel.type: one of free-local | compute-time | per-minute | per-token | per-page | custom");

        if (costModel.Currency is not null && !CurrencyPattern.IsMatch(costModel.Currency))
            errors.Add("costModel.currency: must be a three-letter ISO code");

        if (costModel.UnitPrice is < 0)
            errors.Add("costModel.unitPrice: cannot be negative");

        if (costModel.Type == ProcessorCostModelType.FreeLocal && costModel.UnitPrice is > 0)
            errors.Add("costModel: a free-local processor cannot declare a unit price");
    }
}
