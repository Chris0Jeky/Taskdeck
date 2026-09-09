using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Processing.Policy;

/// <summary>
/// The immutable constraints in force when a processing job is created.
/// CF-10 will produce snapshots; CF-03 job persistence will retain only this snapshot's digest.
/// </summary>
public sealed class ProcessingPolicySnapshot
{
    public const int SchemaVersion = 1;
    public const int MaxProcessorIdLength = 120;

    private static readonly Regex ProcessorIdPattern = new("^[a-z0-9]+(?:[._-][a-z0-9]+)*$", RegexOptions.Compiled);

    public ProcessingPolicySnapshot(
        ProcessingEgressClass egressClass,
        IEnumerable<string> allowedProcessorIds,
        bool allowDiarisation,
        bool allowAlignment,
        DateTimeOffset? deadlineUtc,
        ProcessingCostCeiling? costCeiling)
    {
        if (!Enum.IsDefined(egressClass))
            throw new ArgumentOutOfRangeException(nameof(egressClass), egressClass, "The egress class must be a defined ProcessingEgressClass value.");

        ArgumentNullException.ThrowIfNull(allowedProcessorIds);
        EgressClass = egressClass;
        AllowedProcessorIds = CanonicalizeProcessorIds(allowedProcessorIds);
        AllowDiarisation = allowDiarisation;
        AllowAlignment = allowAlignment;

        if (deadlineUtc is { Offset: var offset } && offset != TimeSpan.Zero)
            throw new ArgumentException("The deadline must use a UTC offset.", nameof(deadlineUtc));

        DeadlineUtc = deadlineUtc;
        CostCeiling = costCeiling;
    }

    public ProcessingEgressClass EgressClass { get; }

    /// <summary>
    /// An allowlist set. Input order and repeated entries do not change the policy or its digest.
    /// Ordered route preferences are a future, distinct field.
    /// </summary>
    public ImmutableArray<string> AllowedProcessorIds { get; }

    public bool AllowDiarisation { get; }

    public bool AllowAlignment { get; }

    /// <summary>
    /// The absolute UTC deadline for a job, or <see langword="null"/> when the policy sets no deadline.
    /// </summary>
    public DateTimeOffset? DeadlineUtc { get; }

    /// <summary>
    /// The maximum charge the policy permits, or <see langword="null"/> when the policy sets no cost limit.
    /// </summary>
    public ProcessingCostCeiling? CostCeiling { get; }

    public string ToCanonicalJson() => ProcessingPolicySnapshotCanonicalizer.Serialize(this);

    public string Digest() => ProcessingPolicySnapshotCanonicalizer.Digest(this);

    private static ImmutableArray<string> CanonicalizeProcessorIds(IEnumerable<string> allowedProcessorIds)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var processorId in allowedProcessorIds)
        {
            if (processorId is null || processorId.Length == 0 || processorId.Length > MaxProcessorIdLength || !ProcessorIdPattern.IsMatch(processorId))
                throw new ArgumentException($"Processor IDs must be non-empty, at most {MaxProcessorIdLength} characters, and match {ProcessorIdPattern}.", nameof(allowedProcessorIds));

            values.Add(processorId);
        }

        return values.Order(StringComparer.Ordinal).ToImmutableArray();
    }
}

/// <summary>
/// A currency-qualified upper bound. A bare number is deliberately not a policy value because a
/// later reader could otherwise interpret the same digest as different currencies.
/// </summary>
public sealed class ProcessingCostCeiling
{
    private static readonly Regex CurrencyPattern = new("^[A-Z]{3}$", RegexOptions.Compiled);

    public ProcessingCostCeiling(decimal amount, string currency)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "The cost ceiling cannot be negative.");
        if (currency is null || !CurrencyPattern.IsMatch(currency))
            throw new ArgumentException("The currency must be a three-letter uppercase ISO 4217 code.", nameof(currency));

        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }
}

/// <summary>
/// Writes the ProcessingPolicySnapshot v1 byte contract. Do not replace this writer with ordinary
/// serializer options: a changed property order or date/decimal formatting changes the digest.
/// </summary>
public static class ProcessingPolicySnapshotCanonicalizer
{
    private const string DigestPrefix = "sha256:";
    private const string UtcTimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private const string DecimalFormat = "0.#############################";

    public static string Serialize(ProcessingPolicySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", ProcessingPolicySnapshot.SchemaVersion);
            writer.WriteString("egressClass", StrictKebabCaseEnumConverterFactory.ToKebabCase(snapshot.EgressClass.ToString()));
            writer.WritePropertyName("allowedProcessorIds");
            writer.WriteStartArray();
            foreach (var processorId in snapshot.AllowedProcessorIds)
                writer.WriteStringValue(processorId);
            writer.WriteEndArray();
            writer.WriteBoolean("allowDiarisation", snapshot.AllowDiarisation);
            writer.WriteBoolean("allowAlignment", snapshot.AllowAlignment);
            WriteDeadline(writer, snapshot.DeadlineUtc);
            WriteCostCeiling(writer, snapshot.CostCeiling);
            writer.WriteEndObject();
            writer.Flush();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static string Digest(ProcessingPolicySnapshot snapshot)
    {
        var json = Serialize(snapshot);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return DigestPrefix + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WriteDeadline(Utf8JsonWriter writer, DateTimeOffset? deadlineUtc)
    {
        writer.WritePropertyName("deadlineUtc");
        if (deadlineUtc is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(deadlineUtc.Value.ToString(UtcTimestampFormat, CultureInfo.InvariantCulture));
    }

    private static void WriteCostCeiling(Utf8JsonWriter writer, ProcessingCostCeiling? costCeiling)
    {
        writer.WritePropertyName("costCeiling");
        if (costCeiling is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("amount");
        writer.WriteRawValue(costCeiling.Amount.ToString(DecimalFormat, CultureInfo.InvariantCulture), skipInputValidation: true);
        writer.WriteString("currency", costCeiling.Currency);
        writer.WriteEndObject();
    }
}
