using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// The immutable header every derived view of a source carries (ADR-0065 §Decision 3). Typed
/// payloads (the shipped <c>Transcript</c> and <c>ArtefactExtraction</c> rows, later OCR and image
/// descriptions) sit behind it; the header is what lineage, caching and evidence anchors reference.
/// <para>
/// <see cref="CaptureId"/> is nullable <b>only for the migration window</b>: shipped Transcript and
/// SourceArtefact rows can exist without a <c>CreatedFromCaptureId</c>, and CF-06 must be able to
/// expose them before CF-01's backfill has created a Capture for every retained legacy source. The
/// target model is non-null — ownership is always known (<see cref="UserId"/> comes from
/// <c>Transcript.UserId</c> / <c>SourceArtefact.UserId</c>), so a durable Capture is backfilled for
/// every retained legacy source and the nullability is removed once no orphan remains.
/// </para>
/// </summary>
public sealed record RepresentationDescriptor(
    Guid Id,
    Guid? CaptureId,
    Guid UserId,
    RepresentationKind Kind,
    Guid? ParentSourceAssetId,
    Guid? ParentRepresentationId,
    Guid? ProcessingRunId,
    string ProcessorId,
    string ProcessorVersion,
    string? ProcessorModel,
    string ConfigurationHash,
    int SchemaVersion,
    string ContentHash,
    string? Language,
    RepresentationQualityState QualityState,
    Guid? SupersededByRepresentationId,
    IReadOnlyList<string> Warnings,
    DateTimeOffset CreatedAt);

/// <summary>
/// Read façade over representations (CF-06 <c>#2260</c>). <b>Draft, not fixed:</b> the shape is
/// scaffolded so the transcript lane (CF-05), evidence anchors (CF-07) and candidates (CF-08) can
/// be written against a stable name, and CF-06's first implementation settles it. The invariants
/// CF-06 must prove before the contract is called fixed (amended 2026-08-30):
/// <list type="number">
/// <item>every representation has exactly one parent — a source asset <i>or</i> a representation,
/// never both, never neither;</item>
/// <item>every retained legacy Transcript / ArtefactExtraction gets a header, and every legacy
/// source without a capture gets a backfilled Capture; a null <see cref="RepresentationDescriptor.CaptureId"/>
/// is a migration-window state, not a permanent one;</item>
/// <item>supersession is a forward link from the old row to the new one; the superseded row and
/// its anchors are never rewritten or deleted by a rerun;</item>
/// <item>quality state is per representation (<see cref="RepresentationQualityState"/>): a
/// provisional streaming partial and its final replacement are two rows;</item>
/// <item>typed payload ownership is by kind — the header never carries a JSON blob payload;</item>
/// <item>the façade is read-only during the migration window; the write path (headers created by
/// the CF-03 runner from processor outputs) lands with CF-06 and the transcript lane adapts to it
/// in CF-05.</item>
/// </list>
/// No implementation is registered yet — resolving this interface before CF-06 lands is a wiring
/// error, not a fallback.
/// </summary>
public interface IRepresentationStore
{
    Task<IReadOnlyList<RepresentationDescriptor>> ListByCaptureAsync(
        Guid captureId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<RepresentationDescriptor?> GetAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default);
}
