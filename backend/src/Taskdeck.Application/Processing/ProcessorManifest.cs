using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Processing;

/// <summary>
/// A processor's self-description (ADR-0065 §Decision 6; CF-04 <c>#2258</c>). Mirrors
/// <c>Processing/Schemas/processor-manifest.v1.schema.json</c> field for field; the schema is the
/// contract, this record is its typed shadow, and <see cref="ProcessorManifestValidator"/> enforces
/// the rules the schema cannot express (capability vocabulary, locality/network consistency, the
/// externalizable-capability rule, contract-per-capability). Output schemas are declared
/// <b>per capability</b> in <see cref="CapabilityContracts"/> (amended 2026-08-30), not globally.
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
    IReadOnlyDictionary<string, ProcessorCapabilityContract>? CapabilityContracts)
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

/// <summary>
/// What one declared capability produces and accepts: the output families it emits
/// (<c>representation</c> | <c>candidate-batch</c> | <c>diagnostic</c>), the schema identifiers of
/// those outputs, and optionally the schema of its capability-specific run options.
/// </summary>
public sealed record ProcessorCapabilityContract(
    IReadOnlyList<string>? Outputs,
    IReadOnlyList<string>? OutputSchemas,
    string? OptionsSchema);

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
/// Serializer settings for manifests (camelCase, exact kebab-case enums). Unknown members are
/// rejected at parse time, which is the runtime form of the schema's <c>additionalProperties: false</c>;
/// the schema file itself is the published contract and is not evaluated at runtime.
/// </summary>
public static class ProcessorManifestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Exact names, exact spellings: the schema requires camelCase members and forbids extras,
        // and enumerations are kebab-case strings, never integers and never case variants.
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new StrictKebabCaseEnumConverterFactory() }
    };
}

/// <summary>
/// The published manifest schema and its canonical example, embedded in the assembly so tests and
/// tooling read the same bytes the repository ships (CF-04 residual: a verbatim copy in a test is
/// silent drift).
/// </summary>
public static class ProcessorManifestResources
{
    public const string SchemaResourceName = "Taskdeck.Application.Processing.Schemas.processor-manifest.v1.schema.json";
    public const string WhisperXExampleResourceName = "Taskdeck.Application.Processing.Schemas.whisperx-processor.example.json";

    public static string ReadSchema() => Read(SchemaResourceName);

    public static string ReadWhisperXExample() => Read(WhisperXExampleResourceName);

    private static string Read(string resourceName)
    {
        using var stream = typeof(ProcessorManifestResources).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' is missing");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// Enum converter that accepts exactly the kebab-case spelling of each member (<c>in-process</c>,
/// <c>free-local</c>) and nothing else: no integers, no case variants, no PascalCase names. The
/// built-in <see cref="JsonStringEnumConverter"/> is case-insensitive, which let <c>"SIDECAR"</c>
/// pass a contract that publishes <c>"sidecar"</c>.
/// </summary>
public sealed class StrictKebabCaseEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(StrictKebabCaseEnumConverter<>).MakeGenericType(typeToConvert))!;

    public static string ToKebabCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsUpper(character) && index > 0 && (char.IsLower(name[index - 1]) || char.IsDigit(name[index - 1])))
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private sealed class StrictKebabCaseEnumConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        private static readonly Dictionary<string, TEnum> ByName = Enum.GetValues<TEnum>()
            .ToDictionary(value => ToKebabCase(value.ToString()), value => value, StringComparer.Ordinal);

        private static readonly Dictionary<TEnum, string> ByValue = ByName
            .ToDictionary(pair => pair.Value, pair => pair.Key);

        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Expected a kebab-case string for {typeof(TEnum).Name}");
            }

            var token = reader.GetString();
            if (token is null || !ByName.TryGetValue(token, out var value))
            {
                throw new JsonException($"'{token}' is not a {typeof(TEnum).Name} value; expected one of {string.Join(" | ", ByName.Keys)}");
            }

            return value;
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(ByValue.TryGetValue(value, out var name) ? name : ToKebabCase(value.ToString()));
        }
    }
}
