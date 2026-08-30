using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Enums;

/// <summary>
/// The three provenance dimensions a legacy <see cref="CaptureSource"/> value collapses.
/// </summary>
public readonly record struct CaptureDimensions(
    CaptureModality Modality,
    CaptureOriginAdapter Origin,
    CaptureProducerKind Producer);

/// <summary>
/// Deterministic mapping between the overloaded legacy <see cref="CaptureSource"/> enum and the
/// independent capture dimensions of ADR-0065 §Decision 2. The forward direction is total and
/// exhaustive (every legacy value maps); the reverse direction is a best effort for compatibility
/// clients and is lossy where the legacy enum drew distinctions the dimensions do not
/// (<see cref="IsAmbiguousLegacySource"/>). No new <see cref="CaptureSource"/> value may be added
/// without a row here — the test suite enumerates the enum.
/// </summary>
public static class CaptureSourceMapping
{
    private static readonly IReadOnlyDictionary<CaptureSource, CaptureDimensions> Forward =
        new Dictionary<CaptureSource, CaptureDimensions>
        {
            [CaptureSource.Typed] = new(CaptureModality.Text, CaptureOriginAdapter.WebComposer, CaptureProducerKind.Human),
            [CaptureSource.Paste] = new(CaptureModality.Text, CaptureOriginAdapter.WebComposer, CaptureProducerKind.Human),
            [CaptureSource.TranscriptPaste] = new(CaptureModality.Text, CaptureOriginAdapter.WebComposer, CaptureProducerKind.Human),
            [CaptureSource.Import] = new(CaptureModality.Document, CaptureOriginAdapter.Import, CaptureProducerKind.Import),
            [CaptureSource.Voice] = new(CaptureModality.Audio, CaptureOriginAdapter.WebComposer, CaptureProducerKind.Human),
            [CaptureSource.MeetingIntegration] = new(CaptureModality.Text, CaptureOriginAdapter.Integration, CaptureProducerKind.Integration),
            [CaptureSource.TranscriptFile] = new(CaptureModality.Document, CaptureOriginAdapter.FileUpload, CaptureProducerKind.Human),
            [CaptureSource.MarkdownImport] = new(CaptureModality.Document, CaptureOriginAdapter.Import, CaptureProducerKind.Import),
            [CaptureSource.WebClip] = new(CaptureModality.Text, CaptureOriginAdapter.BrowserExtension, CaptureProducerKind.Human),
            [CaptureSource.ShareTarget] = new(CaptureModality.Text, CaptureOriginAdapter.ShareTarget, CaptureProducerKind.Human),
            [CaptureSource.BrowserExtension] = new(CaptureModality.Text, CaptureOriginAdapter.BrowserExtension, CaptureProducerKind.Human),
            [CaptureSource.VsCodeExtension] = new(CaptureModality.Text, CaptureOriginAdapter.VsCodeExtension, CaptureProducerKind.Human)
        };

    /// <summary>
    /// Resolves the dimensions for a legacy source. Throws for a value without a row so a new enum
    /// member cannot silently route as "text from the composer".
    /// </summary>
    public static CaptureDimensions Resolve(CaptureSource source)
    {
        if (!Forward.TryGetValue(source, out var dimensions))
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Capture source '{source}' has no dimension mapping");
        }

        return dimensions;
    }

    /// <summary>
    /// Best-effort reverse mapping for compatibility readers. <paramref name="transcriptHint"/>
    /// distinguishes transcript-shaped text from ordinary text where the legacy enum did.
    /// </summary>
    public static CaptureSource ToLegacySource(
        CaptureModality modality,
        CaptureOriginAdapter origin,
        CaptureProducerKind producer,
        bool transcriptHint = false)
    {
        if (modality == CaptureModality.Audio)
        {
            return CaptureSource.Voice;
        }

        if (origin == CaptureOriginAdapter.Integration || producer == CaptureProducerKind.Integration)
        {
            return CaptureSource.MeetingIntegration;
        }

        if (origin == CaptureOriginAdapter.Import || producer == CaptureProducerKind.Import)
        {
            return CaptureSource.Import;
        }

        return origin switch
        {
            CaptureOriginAdapter.ShareTarget => CaptureSource.ShareTarget,
            CaptureOriginAdapter.BrowserExtension => CaptureSource.BrowserExtension,
            CaptureOriginAdapter.VsCodeExtension => CaptureSource.VsCodeExtension,
            CaptureOriginAdapter.FileUpload => transcriptHint || modality == CaptureModality.Document
                ? CaptureSource.TranscriptFile
                : CaptureSource.Import,
            _ => transcriptHint ? CaptureSource.TranscriptPaste : CaptureSource.Typed
        };
    }

    /// <summary>
    /// Legacy values whose distinction the dimensions do not preserve; a round trip through
    /// <see cref="Resolve"/> and <see cref="ToLegacySource"/> returns a sibling value for these.
    /// </summary>
    public static bool IsAmbiguousLegacySource(CaptureSource source) =>
        source is CaptureSource.Paste
            or CaptureSource.TranscriptPaste
            or CaptureSource.WebClip
            or CaptureSource.MarkdownImport;
}
