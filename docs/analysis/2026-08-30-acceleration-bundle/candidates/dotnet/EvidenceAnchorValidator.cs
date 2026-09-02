using System;
using System.Text.RegularExpressions;

namespace Taskdeck.Acceleration.Candidates.ContextFabric;

public enum CandidateEvidenceAnchorKind
{
    TextSpan,
    TimeRange,
    PageRegion,
    ImageRegion,
    JsonPointer,
    WholeSource
}

public sealed record NormalizedRectangle(double X, double Y, double Width, double Height);

public sealed record CandidateEvidenceAnchor(
    CandidateEvidenceAnchorKind Kind,
    int? StartOffset = null,
    int? EndOffset = null,
    long? StartMilliseconds = null,
    long? EndMilliseconds = null,
    int? PageNumber = null,
    NormalizedRectangle? Rectangle = null,
    string? JsonPointer = null,
    string? QuoteSha256 = null);

public sealed record AnchorValidationResult(bool IsValid, string? ErrorCode = null)
{
    public static AnchorValidationResult Valid() => new(true);
    public static AnchorValidationResult Invalid(string code) => new(false, code);
}

public static partial class EvidenceAnchorValidator
{
    public static AnchorValidationResult Validate(CandidateEvidenceAnchor anchor)
    {
        if (anchor.QuoteSha256 is not null && !Sha256Regex().IsMatch(anchor.QuoteSha256))
        {
            return AnchorValidationResult.Invalid("evidence_quote_hash_invalid");
        }

        return anchor.Kind switch
        {
            CandidateEvidenceAnchorKind.TextSpan => ValidateTextSpan(anchor),
            CandidateEvidenceAnchorKind.TimeRange => ValidateTimeRange(anchor),
            CandidateEvidenceAnchorKind.PageRegion => ValidatePageRegion(anchor),
            CandidateEvidenceAnchorKind.ImageRegion => ValidateImageRegion(anchor),
            CandidateEvidenceAnchorKind.JsonPointer => ValidateJsonPointer(anchor),
            CandidateEvidenceAnchorKind.WholeSource => ValidateWholeSource(anchor),
            _ => AnchorValidationResult.Invalid("evidence_anchor_kind_unknown")
        };
    }

    private static AnchorValidationResult ValidateTextSpan(CandidateEvidenceAnchor anchor)
    {
        if (anchor.StartOffset is not { } start || anchor.EndOffset is not { } end
            || start < 0 || end <= start)
        {
            return AnchorValidationResult.Invalid("evidence_text_span_invalid");
        }

        return HasOnly(anchor, offsets: true)
            ? AnchorValidationResult.Valid()
            : AnchorValidationResult.Invalid("evidence_anchor_fields_mismatch");
    }

    private static AnchorValidationResult ValidateTimeRange(CandidateEvidenceAnchor anchor)
    {
        if (anchor.StartMilliseconds is not { } start || anchor.EndMilliseconds is not { } end
            || start < 0 || end <= start)
        {
            return AnchorValidationResult.Invalid("evidence_time_range_invalid");
        }

        return HasOnly(anchor, times: true)
            ? AnchorValidationResult.Valid()
            : AnchorValidationResult.Invalid("evidence_anchor_fields_mismatch");
    }

    private static AnchorValidationResult ValidatePageRegion(CandidateEvidenceAnchor anchor)
    {
        if (anchor.PageNumber is not { } page || page < 1 || !ValidRectangle(anchor.Rectangle))
        {
            return AnchorValidationResult.Invalid("evidence_page_region_invalid");
        }

        return HasOnly(anchor, page: true, rectangle: true)
            ? AnchorValidationResult.Valid()
            : AnchorValidationResult.Invalid("evidence_anchor_fields_mismatch");
    }

    private static AnchorValidationResult ValidateImageRegion(CandidateEvidenceAnchor anchor)
    {
        if (!ValidRectangle(anchor.Rectangle))
        {
            return AnchorValidationResult.Invalid("evidence_image_region_invalid");
        }

        return HasOnly(anchor, rectangle: true)
            ? AnchorValidationResult.Valid()
            : AnchorValidationResult.Invalid("evidence_anchor_fields_mismatch");
    }

    private static AnchorValidationResult ValidateJsonPointer(CandidateEvidenceAnchor anchor)
    {
        if (string.IsNullOrEmpty(anchor.JsonPointer)
            || anchor.JsonPointer[0] != '/'
            || !HasValidJsonPointerEscapes(anchor.JsonPointer))
        {
            return AnchorValidationResult.Invalid("evidence_json_pointer_invalid");
        }

        return HasOnly(anchor, jsonPointer: true)
            ? AnchorValidationResult.Valid()
            : AnchorValidationResult.Invalid("evidence_anchor_fields_mismatch");
    }

    private static AnchorValidationResult ValidateWholeSource(CandidateEvidenceAnchor anchor)
    {
        return HasOnly(anchor)
            ? AnchorValidationResult.Valid()
            : AnchorValidationResult.Invalid("evidence_anchor_fields_mismatch");
    }

    private static bool ValidRectangle(NormalizedRectangle? rectangle)
    {
        if (rectangle is null) return false;
        if (!double.IsFinite(rectangle.X) || !double.IsFinite(rectangle.Y)
            || !double.IsFinite(rectangle.Width) || !double.IsFinite(rectangle.Height))
        {
            return false;
        }

        return rectangle.X >= 0 && rectangle.Y >= 0
               && rectangle.Width > 0 && rectangle.Height > 0
               && rectangle.X + rectangle.Width <= 1
               && rectangle.Y + rectangle.Height <= 1;
    }

    private static bool HasOnly(
        CandidateEvidenceAnchor anchor,
        bool offsets = false,
        bool times = false,
        bool page = false,
        bool rectangle = false,
        bool jsonPointer = false)
    {
        return (offsets || (anchor.StartOffset is null && anchor.EndOffset is null))
               && (times || (anchor.StartMilliseconds is null && anchor.EndMilliseconds is null))
               && (page || anchor.PageNumber is null)
               && (rectangle || anchor.Rectangle is null)
               && (jsonPointer || anchor.JsonPointer is null);
    }

    private static bool HasValidJsonPointerEscapes(string pointer)
    {
        for (var i = 0; i < pointer.Length; i++)
        {
            if (pointer[i] != '~') continue;
            if (i + 1 >= pointer.Length || (pointer[i + 1] != '0' && pointer[i + 1] != '1'))
            {
                return false;
            }
            i++;
        }
        return true;
    }

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}
