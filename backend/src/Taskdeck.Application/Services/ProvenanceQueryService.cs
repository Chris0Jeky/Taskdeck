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

    public ProvenanceQueryService(IProposalProvenanceRepository provenanceRepository)
    {
        _provenanceRepository = provenanceRepository ?? throw new ArgumentNullException(nameof(provenanceRepository));
    }

    public async Task<Result<IReadOnlyList<ProvenanceRowDto>>> GetProvenanceRowsAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        if (proposalId == Guid.Empty)
            return Result.Failure<IReadOnlyList<ProvenanceRowDto>>(
                ErrorCodes.ValidationError,
                "ProposalId cannot be empty");

        var provenance = await _provenanceRepository.GetByProposalIdAsync(proposalId, cancellationToken);

        if (provenance is null)
        {
            // No provenance recorded for this proposal -- return empty list, not an error.
            return Result.Success<IReadOnlyList<ProvenanceRowDto>>(
                Array.Empty<ProvenanceRowDto>());
        }

        var rows = provenance.Fields
            .Select(MapFieldToRow)
            .ToList()
            .AsReadOnly();

        return Result.Success<IReadOnlyList<ProvenanceRowDto>>(rows);
    }

    internal static ProvenanceRowDto MapFieldToRow(ProvenanceField field)
    {
        var icon = ResolveIcon(field.FieldName);
        var key = field.FieldName;
        var value = BuildValue(field);
        var weight = MapWeight(field.Kind, field.Confidence);
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
                link.SpanEnd))
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
    /// Builds a human-readable value string from the provenance field.
    /// For extractive fields, includes the quote snippet.
    /// For inferred fields, notes the confidence level.
    /// </summary>
    internal static string BuildValue(ProvenanceField field)
    {
        var confidencePercent = (int)Math.Round(field.Confidence * 100);

        if (field.Kind == ProvenanceKind.Extractive && !string.IsNullOrWhiteSpace(field.ExtractiveQuote))
        {
            // Truncate long quotes to keep the UI scannable.
            var quote = field.ExtractiveQuote.Length > 120
                ? string.Concat(field.ExtractiveQuote.AsSpan(0, 117), "...")
                : field.ExtractiveQuote;
            return $"Extracted: \"{quote}\" ({confidencePercent}% match)";
        }

        if (field.Kind == ProvenanceKind.Inferred)
        {
            return $"Inferred by model ({confidencePercent}% confidence)";
        }

        // Fallback for extractive fields without a quote (should not happen per domain rules,
        // but we handle it defensively).
        return $"Source field ({confidencePercent}% confidence)";
    }

    /// <summary>
    /// Maps the domain <see cref="ProvenanceKind"/> and confidence score to
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
    internal static string MapWeight(ProvenanceKind kind, double confidence)
    {
        return kind switch
        {
            ProvenanceKind.Inferred => "inferred",
            ProvenanceKind.Extractive => confidence >= 0.7 ? "primary" : "contextual",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unrecognized ProvenanceKind")
        };
    }
}
