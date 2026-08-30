using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// The immutable header every derived view of a source carries (ADR-0065 §Decision 3). Typed
/// payloads (the shipped <c>Transcript</c> and <c>ArtefactExtraction</c> rows, later OCR and image
/// descriptions) sit behind it; the header is what lineage, caching and evidence anchors reference.
/// </summary>
public sealed record RepresentationDescriptor(
    Guid Id,
    Guid CaptureId,
    RepresentationKind Kind,
    Guid? ParentSourceAssetId,
    Guid? ParentRepresentationId,
    Guid? ProcessingRunId,
    string ProcessorId,
    string ProcessorVersion,
    int SchemaVersion,
    string ContentHash,
    string? Language,
    IReadOnlyList<string> Warnings,
    DateTimeOffset CreatedAt);

/// <summary>
/// Read façade over representations (CF-06 <c>#2260</c>). The contract is fixed here so the
/// transcript lane (CF-05), evidence anchors (CF-07) and candidates (CF-08) can be written against
/// it; the first implementation adapts the shipped Transcript and ArtefactExtraction tables without a
/// destructive migration. No implementation is registered yet — resolving this interface before
/// CF-06 lands is a wiring error, not a fallback.
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
