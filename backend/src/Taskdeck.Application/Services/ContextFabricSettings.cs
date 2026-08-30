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
    /// <c>SourceAsset</c> and its locator as an external-reference asset. Turning it off stops new
    /// captures reaching the aggregate, which also disarms the Inbox read switch: reads fall back to
    /// the queue row, so nothing disappears, but the durable table stops tracking new work.
    /// </summary>
    public bool DualWriteCaptures { get; set; } = true;

    /// <summary>
    /// When true (the default), the ID-preserving backfill of pre-existing capture queue rows runs
    /// after migrations at startup (<see cref="CaptureBackfillService"/>). It is idempotent and
    /// resumable, so a completed database costs one indexed count per start. Turning it off leaves
    /// the marker incomplete, which keeps Inbox reads on the queue row.
    /// </summary>
    public bool BackfillCaptures { get; set; } = true;

    /// <summary>
    /// When true (the default), Inbox list / get / summary resolve a capture's own material - its
    /// source text, its capture source and its intake time - from the durable aggregate through
    /// <c>ICaptureStore</c> instead of parsing the queue row's payload JSON. The switch is armed
    /// only once the backfill marker records completion, and it degrades per item: a capture with no
    /// durable row is still read from its queue row, so a capture can never disappear from the
    /// Inbox. Set to false to force every read back onto the queue row without touching the
    /// dual-write.
    /// </summary>
    public bool ReadCapturesFromStore { get; set; } = true;
}
