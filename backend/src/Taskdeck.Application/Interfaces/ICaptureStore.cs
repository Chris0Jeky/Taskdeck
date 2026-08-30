using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// What the Inbox listing needs from a durable <see cref="Capture"/>, and nothing more (CF-01
/// <c>#2255</c>). <c>CurrentText</c> is the text of the active inline source asset - the one nothing
/// has superseded - or null when the capture carries no inline text. <c>UpdatedAt</c> lets the read
/// path tell an aggregate that agrees with its queue row from one the queue row has moved past.
/// </summary>
public sealed record CaptureListMaterial(
    Guid Id,
    CaptureSource LegacySourceSnapshot,
    DateTimeOffset CapturedAtServer,
    DateTimeOffset UpdatedAt,
    string? CurrentText);

/// <summary>
/// The persistence façade for the durable <see cref="Capture"/> aggregate (ADR-0065 §Decision 1;
/// CF-01 <c>#2255</c>). Inbox reads move behind this contract once the ID-preserving backfill ships;
/// until then it only receives the dual-written mirrors of legacy queue captures. Writes are
/// staged into the ambient unit of work — callers persist through
/// <see cref="IUnitOfWork.SaveChangesAsync"/>, so a capture mirror and its queue row commit together.
/// </summary>
public interface ICaptureStore
{
    Task AddAsync(Capture capture, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one capture, owner-scoped, for display. Detached: nothing a caller mutates on the
    /// returned aggregate is persisted. Use <see cref="GetByIdForUpdateAsync"/> to change it.
    /// </summary>
    Task<Capture?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one capture, owner-scoped, <b>tracked</b>, so the aggregate's own mutators
    /// (<c>Retitle</c>, <c>SetRequestedIntent</c>, <c>RecordProcessingSummary</c>,
    /// <c>SupersedeInlineTextSource</c>) commit through the ambient unit of work. Its source assets
    /// and their text payloads are loaded with it, so a superseding edit sees the current source.
    /// </summary>
    Task<Capture?> GetByIdForUpdateAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages the changes made to a tracked aggregate. Explicit rather than implicit so a caller
    /// states its intent to write; the actual write happens in
    /// <see cref="IUnitOfWork.SaveChangesAsync"/> with everything else in the same transaction.
    /// </summary>
    Task UpdateAsync(Capture capture, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a page of captures the caller already identified by id. Owner-scoped and detached; ids
    /// without a capture are simply absent from the result. Loads the whole aggregate, so it belongs
    /// to detail paths and to the export - the Inbox listing uses
    /// <see cref="GetListMaterialForUserAsync"/>, which never materialises superseded text.
    /// </summary>
    Task<IReadOnlyList<Capture>> GetByIdsForUserAsync(
        IReadOnlyCollection<Guid> ids,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The narrow projection the Inbox listing needs for a page of ids: identity, the source
    /// snapshot, the intake time, the aggregate update stamp (the read path compares it with the
    /// queue row to detect divergence) and the text of the one active inline asset.
    /// <para>
    /// A dedicated projection rather than the aggregate, because a listing must not pay for the
    /// correction history of every item on the page: loading the full graph materialises every
    /// superseded revision, each up to 200k characters, to render a 200-character excerpt.
    /// </para>
    /// Ids without a capture are absent, which is how the read path detects a gap in the backfill
    /// and falls back to the queue row for that item.
    /// </summary>
    Task<IReadOnlyList<CaptureListMaterial>> GetListMaterialForUserAsync(
        IReadOnlyCollection<Guid> ids,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> CountByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every capture owned by <paramref name="userId"/> (account erasure). Set-based and
    /// executed inside the caller's ambient transaction; returns the number of rows removed.
    /// </summary>
    Task<int> DeleteByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
