using System.Security.Cryptography;
using System.Text;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Immutable derived-content header. It contains no payload and is not yet mapped to persistence.
/// Parent existence, authorization and transaction integrity remain the store's responsibility.
/// </summary>
public sealed class Representation
{
    public Guid Id { get; }
    public Guid? CaptureId { get; }
    public Guid UserId { get; }
    public RepresentationKind Kind { get; }
    public Guid? ParentSourceAssetId { get; }
    public Guid? ParentRepresentationId { get; }
    public Guid? ProcessingRunId { get; }
    public string ProcessorId { get; }
    public string ProcessorVersion { get; }
    public string? ProcessorModel { get; }
    public string ConfigurationHash { get; }
    public int SchemaVersion { get; }
    public string ContentHash { get; }
    public string? Language { get; }
    public RepresentationQualityState QualityState { get; }
    public IReadOnlyList<string> Warnings { get; }
    public DateTimeOffset CreatedAt { get; }

    public Representation(
        Guid id, Guid? captureId, Guid userId, RepresentationKind kind,
        Guid? parentSourceAssetId, Guid? parentRepresentationId, Guid? processingRunId,
        string processorId, string processorVersion, string? processorModel,
        string configurationHash, int schemaVersion, string contentHash, string? language,
        RepresentationQualityState qualityState, IEnumerable<string> warnings,
        DateTimeOffset createdAt, bool isLegacyMigration = false)
    {
        Require(id != Guid.Empty && userId != Guid.Empty, "Representation and owner IDs are required");
        Require(captureId != Guid.Empty && (captureId.HasValue || isLegacyMigration),
            "Capture ID is required outside the legacy migration window");
        Require(parentSourceAssetId.HasValue != parentRepresentationId.HasValue,
            "Exactly one source asset or representation parent is required");
        Require(parentSourceAssetId != Guid.Empty && parentRepresentationId != Guid.Empty,
            "Parent IDs cannot be empty");
        Require(parentRepresentationId != id, "A representation cannot derive from itself");
        Require(processingRunId != Guid.Empty, "Processing run ID cannot be empty");
        Require(Enum.IsDefined(kind), "Representation kind is invalid");
        Require(Enum.IsDefined(qualityState) && qualityState != RepresentationQualityState.Superseded,
            "Row quality must be Provisional, Final or Verified; supersession is a separate forward link");
        Require(!string.IsNullOrWhiteSpace(processorId) && !string.IsNullOrWhiteSpace(processorVersion),
            "Processor identity and version are required");
        Require(schemaVersion > 0, "Schema version must be positive");
        Require(createdAt != default, "Creation time is required");
        ArgumentNullException.ThrowIfNull(warnings);
        var warningArray = warnings.ToArray();
        Require(warningArray.All(warning => warning is not null), "Warnings cannot contain null entries");

        Id = id;
        CaptureId = captureId;
        UserId = userId;
        Kind = kind;
        ParentSourceAssetId = parentSourceAssetId;
        ParentRepresentationId = parentRepresentationId;
        ProcessingRunId = processingRunId;
        ProcessorId = processorId;
        ProcessorVersion = processorVersion;
        ProcessorModel = processorModel;
        ConfigurationHash = ValidateHash(configurationHash);
        SchemaVersion = schemaVersion;
        ContentHash = ValidateHash(contentHash);
        Language = language;
        QualityState = qualityState;
        Warnings = Array.AsReadOnly(warningArray);
        CreatedAt = createdAt;
    }

    /// <summary>Checks supplied lineage snapshots; does not load or authorize them.</summary>
    public void ValidateParent(SourceAsset asset, Capture capture)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(capture);
        Require(ParentSourceAssetId == asset.Id && asset.CaptureId == capture.Id &&
            CaptureId == capture.Id && UserId == capture.UserId,
            "Source parent must resolve to the same capture and owner");
    }

    /// <summary>Null capture lineage remains unresolved during migration and cannot pass this check.</summary>
    public void ValidateParent(Representation parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        Require(ParentRepresentationId == parent.Id && CaptureId.HasValue &&
            CaptureId == parent.CaptureId && UserId == parent.UserId,
            "Representation parent must resolve to the same capture and owner");
    }

    public void ValidatePayload(Transcript payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Require(Kind == RepresentationKind.Transcript && Id == payload.Id && UserId == payload.UserId,
            "Transcript payload must match the header kind, preserved ID and owner");
    }

    /// <summary>Extraction ownership must additionally resolve through its SourceArtefact in the store.</summary>
    public void ValidatePayload(ArtefactExtraction payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Require(Kind == RepresentationKind.NormalizedText && Id == payload.Id,
            "Extraction payload must match the header kind and preserved ID");
    }

    /// <summary>
    /// SHA-256 over strict UTF-8 of LF-normalized text, without trimming or Unicode normalization.
    /// Structured payloads require their own versioned canonical serializer before hashing.
    /// </summary>
    public static string ComputeTextContentHash(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        try
        {
            return Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(normalized)))
                .ToLowerInvariant();
        }
        catch (EncoderFallbackException)
        {
            throw new DomainException(ErrorCodes.ValidationError, "Representation text must contain valid UTF-16");
        }
    }

    private static string ValidateHash(string hash)
    {
        Require(hash is not null && hash.Length == 64 && hash.All(Uri.IsHexDigit),
            "Hash must be a 64-character hexadecimal SHA-256 digest");
        return hash!.ToLowerInvariant();
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
            throw new DomainException(ErrorCodes.ValidationError, message);
    }
}

/// <summary>
/// A forward edge, not a mutation of the old header. Persistence must enforce one edge per old ID
/// and validate both endpoints in the same transaction; this object proves only supplied snapshots.
/// </summary>
public sealed class RepresentationSupersession
{
    public Guid RepresentationId { get; }
    public Guid SupersededByRepresentationId { get; }

    public RepresentationSupersession(Representation previous, Representation replacement)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(replacement);
        Representation.Require(previous.Id != replacement.Id,
            "Supersession must point to a distinct replacement representation");
        Representation.Require(previous.CaptureId.HasValue && previous.CaptureId == replacement.CaptureId &&
            previous.UserId == replacement.UserId && previous.Kind == replacement.Kind,
            "Supersession requires the same resolved capture, owner and kind");
        RepresentationId = previous.Id;
        SupersededByRepresentationId = replacement.Id;
    }
}
