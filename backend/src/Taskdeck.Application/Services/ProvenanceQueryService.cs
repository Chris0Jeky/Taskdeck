using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Maps <see cref="ProposalProvenance"/> domain entities to the Paper
/// deep-Review provenance row shape consumed by the frontend.
/// </summary>
public class ProvenanceQueryService : IProvenanceQueryService
{
    private readonly IProposalProvenanceRepository _provenanceRepository;
    private readonly ITranscriptRepository _transcriptRepository;

    /// <summary>
    /// Stable mapping from canonical field name (lower-cased) to emoji icon.
    /// Unknown field names fall back to a generic icon.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> IconMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = "\U0001F4DD",          // memo
            ["description"] = "\U0001F4C4",     // page facing up
            ["card body"] = "\U0001F4C4",       // page facing up
            ["body"] = "\U0001F4C4",            // page facing up
            ["label"] = "\U0001F3F7️",     // label
            ["labels"] = "\U0001F3F7️",    // label
            ["column"] = "\U0001F4CA",           // bar chart
            ["due date"] = "\U0001F4C5",         // calendar
            ["duedate"] = "\U0001F4C5",          // calendar
            ["due_date"] = "\U0001F4C5",         // calendar
            ["assignee"] = "\U0001F464",         // bust in silhouette
            ["priority"] = "\U0001F6A9",         // triangular flag
            ["board activity"] = "\U0001F4DC",   // scroll
            ["activity"] = "\U0001F4DC",         // scroll
            ["checklist"] = "\U00002705",        // white heavy check mark
            ["subtask"] = "\U00002705",          // white heavy check mark
            ["subtasks"] = "\U00002705",         // white heavy check mark
            ["comment"] = "\U0001F4AC",          // speech balloon
            ["comments"] = "\U0001F4AC",         // speech balloon
            ["attachment"] = "\U0001F4CE",       // paperclip
            ["link"] = "\U0001F517",             // link symbol
            ["design-doc"] = "\U0001F517",       // link symbol
            ["capture"] = "\U0001F4E5",          // inbox tray
            ["inbox"] = "\U0001F4E5",            // inbox tray
            ["not read"] = "\U00002298",         // circled division slash
            ["excluded"] = "\U00002298",         // circled division slash
            ["inferred"] = "\U00002726",         // four pointed star (✦)
        };

    /// <summary>
    /// Fallback icon when no mapping exists for the field name.
    /// </summary>
    internal const string DefaultIcon = "\U0001F4C4"; // page facing up

    public ProvenanceQueryService(
        IProposalProvenanceRepository provenanceRepository,
        ITranscriptRepository transcriptRepository)
    {
        _provenanceRepository = provenanceRepository ?? throw new ArgumentNullException(nameof(provenanceRepository));
        _transcriptRepository = transcriptRepository ?? throw new ArgumentNullException(nameof(transcriptRepository));
    }

    public async Task<Result<IReadOnlyList<ProvenanceRowDto>>> GetProvenanceRowsAsync(
        Guid proposalId,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        if (proposalId == Guid.Empty)
            return Result.Failure<IReadOnlyList<ProvenanceRowDto>>(
                ErrorCodes.ValidationError,
                "ProposalId cannot be empty");

        if (callerUserId == Guid.Empty)
            return Result.Failure<IReadOnlyList<ProvenanceRowDto>>(
                ErrorCodes.ValidationError,
                "CallerUserId cannot be empty");

        var provenance = await _provenanceRepository.GetByProposalIdAsync(proposalId, cancellationToken);

        if (provenance is null)
        {
            // No provenance recorded for this proposal -- return empty list, not an error.
            return Result.Success<IReadOnlyList<ProvenanceRowDto>>(
                Array.Empty<ProvenanceRowDto>());
        }

        // Which transcripts this caller may actually open is a claims-derived fact, resolved once
        // for the whole payload. Provenance is board-authorized while transcript read is
        // owner-only, so a board collaborator legitimately sees links they cannot follow.
        var ownedTranscriptIds = await ResolveOwnedTranscriptIdsAsync(
            provenance,
            callerUserId,
            cancellationToken);

        var rows = provenance.Fields
            .Select(field => MapFieldToRow(field, ownedTranscriptIds))
            .ToList()
            .AsReadOnly();

        return Result.Success<IReadOnlyList<ProvenanceRowDto>>(rows);
    }

    /// <summary>
    /// Returns the transcript ids referenced by this provenance that <paramref name="callerUserId"/>
    /// owns. Ids the caller does not own are absent, so nothing about another user's data is
    /// disclosed beyond the caller's own access.
    /// </summary>
    private async Task<IReadOnlySet<Guid>> ResolveOwnedTranscriptIdsAsync(
        ProposalProvenance provenance,
        Guid callerUserId,
        CancellationToken cancellationToken)
    {
        var referencedTranscriptIds = provenance.Fields
            .SelectMany(field => field.EvidenceLinks)
            .Where(IsTranscriptEvidence)
            .Select(link => link.TranscriptId!.Value)
            .Distinct()
            .ToList();

        if (referencedTranscriptIds.Count == 0)
            return EmptyTranscriptIds;

        var owned = await _transcriptRepository.FilterOwnedIdsAsync(
            referencedTranscriptIds,
            callerUserId,
            cancellationToken);

        return owned.ToHashSet();
    }

    private static readonly IReadOnlySet<Guid> EmptyTranscriptIds = new HashSet<Guid>();

    /// <summary>
    /// True for a link that names a stored transcript with a typed transcript id — the only
    /// evidence kind with a read endpoint behind the "view in transcript" affordance today.
    /// </summary>
    internal static bool IsTranscriptEvidence(ProvenanceEvidenceLink link) =>
        string.Equals(link.SourceType, ProvenanceEvidenceLink.TranscriptSourceType, StringComparison.Ordinal)
        && link.TranscriptId is { } transcriptId
        && transcriptId != Guid.Empty;

    internal static ProvenanceRowDto MapFieldToRow(ProvenanceField field, IReadOnlySet<Guid> ownedTranscriptIds)
    {
        var icon = ResolveIcon(field.FieldName);
        var key = field.FieldName;
        var value = BuildValue(field);
        var weight = MapWeight(field.Kind, field.Confidence, field.ConfidenceSource);
        var evidenceLinks = field.EvidenceLinks
            .OrderBy(link => link.SourceType, StringComparer.Ordinal)
            .ThenBy(link => link.SourceId, StringComparer.Ordinal)
            .ThenBy(link => link.Label, StringComparer.Ordinal)
            .ThenBy(link => link.SpanStart)
            .ThenBy(link => link.SpanEnd)
            .Select(link => new ProvenanceEvidenceLinkDto(
                link.SourceType,
                link.SourceId,
                link.Label,
                link.SpanStart,
                link.SpanEnd,
                // Fails closed: an evidence kind with no reader, or a transcript this caller does
                // not own, is never advertised as viewable.
                IsTranscriptEvidence(link) && ownedTranscriptIds.Contains(link.TranscriptId!.Value)))
            .ToList()
            .AsReadOnly();

        return new ProvenanceRowDto(icon, key, value, weight, evidenceLinks);
    }

    /// <summary>
    /// Resolves the emoji icon for a field name using the stable icon map.
    /// Falls back to <see cref="DefaultIcon"/> for unknown field names.
    /// </summary>
    internal static string ResolveIcon(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            return DefaultIcon;

        return IconMap.TryGetValue(fieldName, out var icon) ? icon : DefaultIcon;
    }

    /// <summary>
    /// Builds a human-readable value string from the provenance field without presenting a number
    /// unless its persisted source says it was model-reported or algorithmically derived.
    /// </summary>
    internal static string BuildValue(ProvenanceField field)
    {
        if (field.ConfidenceSource == ProvenanceConfidenceSource.Deterministic)
            return "Deterministic extraction (no model confidence)";

        if (field.ConfidenceSource == ProvenanceConfidenceSource.NotReported || field.Confidence is null)
            return "No model confidence reported";

        var confidencePercent = (int)Math.Round(field.Confidence.Value * 100);

        if (field.ConfidenceSource == ProvenanceConfidenceSource.ModelReported)
            return $"Model reported {confidencePercent}% confidence";

        if (field.Kind == ProvenanceKind.Extractive && !string.IsNullOrWhiteSpace(field.ExtractiveQuote))
        {
            // Truncate long quotes to keep the UI scannable.
            var quote = field.ExtractiveQuote.Length > 120
                ? string.Concat(field.ExtractiveQuote.AsSpan(0, 117), "...")
                : field.ExtractiveQuote;
            return $"Extracted: \"{quote}\" ({confidencePercent}% match)";
        }

        if (field.Kind == ProvenanceKind.Inferred)
            return $"Derived confidence: {confidencePercent}%";

        // Fallback for extractive fields without a quote (should not happen per domain rules,
        // but we handle it defensively).
        return $"Source field ({confidencePercent}% confidence)";
    }

    /// <summary>
    /// Maps the domain <see cref="ProvenanceKind"/> and trustworthy confidence score to
    /// the 4-bucket weight system used by the Paper deep-Review surface.
    ///
    /// Buckets:
    ///   - primary:    Extractive with high confidence (>= 0.7)
    ///   - contextual: Extractive with lower confidence (< 0.7)
    ///   - inferred:   Inferred kind regardless of confidence
    ///   - excluded:   Never reached from existing fields, but the frontend
    ///                 defines this bucket for "not read" entries that are
    ///                 synthesized separately.
    ///
    /// Note: "excluded" rows are not currently generated from ProvenanceField
    /// data. The frontend can inject them client-side or a future backend
    /// enhancement can add a dedicated "excluded sources" list.
    /// </summary>
    internal static string MapWeight(
        ProvenanceKind kind,
        double? confidence,
        ProvenanceConfidenceSource confidenceSource)
    {
        return kind switch
        {
            ProvenanceKind.Inferred => "inferred",
            ProvenanceKind.Extractive when
                confidenceSource == ProvenanceConfidenceSource.Derived && confidence >= 0.7 => "primary",
            ProvenanceKind.Extractive => "contextual",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unrecognized ProvenanceKind")
        };
    }

    internal static string MapWeight(ProvenanceKind kind, double confidence) =>
        MapWeight(
            kind,
            confidence,
            kind == ProvenanceKind.Extractive
                ? ProvenanceConfidenceSource.Derived
                : ProvenanceConfidenceSource.ModelReported);

}
