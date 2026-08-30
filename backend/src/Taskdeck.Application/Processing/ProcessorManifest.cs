using System.Text.Json;
using System.Text.Json.Serialization;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Processing;

/// <summary>
/// A processor's self-description (ADR-0065 §Decision 6; CF-04 <c>#2258</c>). Mirrors
/// <c>Processing/Schemas/processor-manifest.v1.schema.json</c> field for field; the schema is the
/// contract, this record is its typed shadow, and <see cref="ProcessorManifestValidator"/> enforces
/// the rules the schema cannot express (capability vocabulary, locality/network consistency).
/// </summary>
public sealed record ProcessorManifest(
    string? Id,
    string? Version,
    string? DisplayName,
    IReadOnlyList<string>? Capabilities,
    ProcessorExecutionMode? Execution,
    ProcessorLocality? Locality,
    IReadOnlyList<string>? Accepts,
    IReadOnlyList<string>? Languages,
    IReadOnlyList<string>? Features,
    ProcessorResourceRequirements? Resources,
    ProcessorPrivacyDeclaration? Privacy,
    ProcessorCostModel? CostModel,
    IReadOnlyList<string>? OutputSchemas)
{
    /// <summary>
    /// Parses manifest JSON leniently: shape errors surface as a parse failure, semantic errors are
    /// left to <see cref="ProcessorManifestValidator"/> so a registry can report every problem at once.
    /// </summary>
    public static bool TryParse(string json, out ProcessorManifest? manifest, out string? error)
    {
        manifest = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Manifest JSON is empty";
            return false;
        }

        try
        {
            manifest = JsonSerializer.Deserialize<ProcessorManifest>(json, ProcessorManifestJson.Options);
        }
        catch (JsonException exception)
        {
            error = $"Manifest JSON is malformed: {exception.Message}";
            return false;
        }

        if (manifest is null)
        {
            error = "Manifest JSON is null";
            return false;
        }

        return true;
    }
}

public sealed record ProcessorResourceRequirements(
    bool? Cpu,
    ProcessorGpuRequirement? Gpu,
    int? MinVramMb,
    int? EstimatedRamMb);

public sealed record ProcessorPrivacyDeclaration(
    bool? NetworkRequired,
    IReadOnlyList<string>? AllowedHosts,
    IReadOnlyList<string>? DataClasses,
    bool? SupportsRegionalRouting);

public sealed record ProcessorCostModel(
    ProcessorCostModelType? Type,
    string? Currency,
    decimal? UnitPrice);

/// <summary>
/// Serializer settings for manifests (camelCase, kebab-case enums). Unknown members are rejected at
/// parse time, which is the runtime form of the schema's <c>additionalProperties: false</c>; the
/// schema file itself is the published contract and is not evaluated at runtime.
/// </summary>
public static class ProcessorManifestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Exact names, exact spellings: the schema requires camelCase members and forbids extras,
        // and enumerations are kebab-case strings, never integers.
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false) }
    };
}
