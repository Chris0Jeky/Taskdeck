namespace Taskdeck.Application.Services;

/// <summary>
/// Settings for the Context Fabric migration (ADR-0065), bound from the <c>ContextFabric</c>
/// configuration section. Each flag is a compatibility switch for a bounded slice; the defaults are
/// the behaviour CF-01 <c>#2255</c> ships, and turning any of them off falls back to reading the
/// legacy queue row rather than changing what a user sees.
/// </summary>
public sealed class ContextFabricSettings
{
    /// <summary>
    /// When true (the default since CF-01 <c>#2255</c>), every capture admitted through
    /// <see cref="CaptureIntakeService"/> is written to the durable <c>Captures</c> aggregate under
    /// the queue row's own id, with its typed or pasted text as an immutable inline
    /// <c>SourceAsset</c> and its locator as an external-reference asset.
    /// <para>
    /// <b>Turning it off is not consequence-free, and turning it back on is not either.</b> While it
    /// is off, captures created in that window never reach the aggregate at all. Captures that
    /// already have one are still kept in step - an edit, a keep, an archive still writes the
    /// aggregate, because a flag about new rows must never license an existing row to rot - but a
    /// durable write that fails is not retried inline. Re-enabling therefore does not simply resume:
    /// the next <see cref="CaptureBackfillService"/> pass has to bring the window's captures in and
    /// reconcile anything that drifted, and until it has, the read path serves whichever of the two
    /// wrote last. Nothing a user sees goes backwards at any point in that sequence.
    /// </para>
    /// </summary>
    public bool DualWriteCaptures { get; set; } = true;

    /// <summary>
    /// When true (the default), the ID-preserving backfill and reconcile pass runs after migrations
    /// at startup (<see cref="CaptureBackfillService"/>). It brings in pre-existing capture queue
    /// rows AND repairs any capture whose queue row has been written since - the case
    /// <see cref="DualWriteCaptures"/> being off for a while creates. It is idempotent and resumable,
    /// so a database whose captures all agree costs one marker read and one indexed count per start.
    /// Turning it off leaves the marker incomplete on a database that has never completed it, which
    /// keeps Inbox reads on the queue row; on a database that has, it leaves drift unrepaired, and
    /// the read path falls back per item to whichever writer moved last.
    /// </summary>
    public bool BackfillCaptures { get; set; } = true;

    /// <summary>
    /// When true (the default), Inbox list / get / summary resolve a capture's own material - its
    /// source text, its capture source and its intake time - from the durable aggregate through
    /// <c>ICaptureStore</c> instead of parsing the queue row's payload JSON. Mutation responses obey
    /// the same gate as reads, so an operator who turns this off never gets aggregate material back
    /// from a keep, an archive or an edit either.
    /// <para>
    /// Three guards, not one. The switch is armed only once the backfill marker records completion;
    /// it degrades per item, so a capture with no durable row is still read from its queue row; and
    /// it defers to the queue row for any capture whose text disagrees with an aggregate the queue
    /// row has been written past. A capture can neither disappear from the Inbox nor go backwards in
    /// it. Set to false to force every read onto the queue row without touching the dual-write.
    /// </para>
    /// </summary>
    public bool ReadCapturesFromStore { get; set; } = true;
}
