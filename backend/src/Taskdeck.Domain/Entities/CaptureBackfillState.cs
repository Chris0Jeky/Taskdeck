using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// The persisted marker for the ID-preserving backfill of legacy capture queue rows into the
/// durable <see cref="Capture"/> aggregate (ADR-0065 §Decision 1; CF-01 <c>#2255</c>).
/// <para>
/// The backfill itself is idempotent and resumable without this row — it selects capture-shaped
/// <see cref="LlmRequest"/> rows that have no <see cref="Capture"/> under the same id, so a crash
/// mid-way simply leaves fewer rows for the next run. The marker exists for the <b>read switch</b>:
/// Inbox reads may only be served from the durable aggregate on a host whose backfill has actually
/// finished, and a host that has never run it (or has it disabled) must keep reading the queue row.
/// Without a durable record of that, a restart could not tell "no legacy rows" from "not migrated
/// yet", and a capture would vanish from the Inbox.
/// </para>
/// </summary>
public sealed class CaptureBackfillState : Entity
{
    /// <summary>
    /// The identity of the marker row. Fixed rather than generated so every host that opens this
    /// database converges on the same row and a concurrent first run collides on the primary key
    /// instead of writing a second, disagreeing marker. The marker describes the <b>database</b>,
    /// never a host or a process: one row per backfill key, shared by the web API, both MCP hosts
    /// and the CLI.
    /// </summary>
    public static readonly Guid LegacyQueueBackfillId = new("2f5c9d41-7f0e-4a63-9d8a-1c6b4f2a9e55");

    public const string LegacyQueueBackfillKey = "capture.legacy-queue.v1";
    public const int MaxKeyLength = 100;
    public const int MaxNoteLength = 500;

    /// <summary>Stable name of the migration this row tracks; unique.</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>When a host first started this backfill.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>When the backlog was observed empty (or unable to shrink further); null while incomplete.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Legacy rows mirrored into the aggregate across every run.</summary>
    public int MigratedCount { get; private set; }

    /// <summary>
    /// Distinct legacy rows the most recent run could not bring in - a snapshot of that run, not a
    /// running total, so a row that fails on every start is counted once rather than once per
    /// attempt. Those rows stay readable through the queue-row fallback, and while any of them
    /// remain the marker stays incomplete.
    /// </summary>
    public int SkippedCount { get; private set; }

    /// <summary>The last skip reason, for the operator; never user content.</summary>
    public string? LastSkipReason { get; private set; }

    /// <summary>The read switch is armed only once the backfill has finished at least once.</summary>
    public bool IsComplete => CompletedAt.HasValue;

    private CaptureBackfillState() : base()
    {
    }

    public CaptureBackfillState(Guid id, string key, DateTimeOffset startedAt)
        : base(id)
    {
        if (id == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Backfill state ID cannot be empty");
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Backfill key is required and cannot exceed {MaxKeyLength} characters");

        Key = key.Trim();
        StartedAt = startedAt;
        CreatedAt = startedAt;
        UpdatedAt = startedAt;
    }

    /// <summary>Creates the singleton marker for the legacy queue backfill in its not-yet-complete state.</summary>
    public static CaptureBackfillState ForLegacyQueue(DateTimeOffset startedAt) =>
        new(LegacyQueueBackfillId, LegacyQueueBackfillKey, startedAt);

    /// <summary>
    /// Adds the outcome of one committed batch. Rows brought in accumulate across runs and restarts;
    /// rows merely reconciled with their queue row do not, because they were counted when they were
    /// first brought in.
    /// </summary>
    public void RecordBatch(int migrated)
    {
        if (migrated < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Backfill counts cannot be negative");

        MigratedCount += migrated;
        Touch();
    }

    /// <summary>
    /// Replaces the skip snapshot with the distinct count this run could not bring in. Assignment,
    /// not accumulation: the same unmappable row must not inflate the number every time a host
    /// starts.
    /// </summary>
    public void RecordSkipped(int distinctSkipped, string? lastSkipReason = null)
    {
        if (distinctSkipped < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Backfill counts cannot be negative");

        SkippedCount = distinctSkipped;
        if (!string.IsNullOrWhiteSpace(lastSkipReason))
        {
            var trimmed = lastSkipReason.Trim();
            LastSkipReason = trimmed.Length > MaxNoteLength ? trimmed[..MaxNoteLength] : trimmed;
        }
        else if (distinctSkipped == 0)
        {
            LastSkipReason = null;
        }

        Touch();
    }

    /// <summary>
    /// Records that the backlog is drained - every capture-shaped queue row is in the aggregate and
    /// none of them has diverged from it. Only a caller that has just observed an empty backlog may
    /// call this: while a single row is outstanding the read switch must stay disarmed, because this
    /// marker is the only thing that separates "this database has no legacy capture rows" from "this
    /// database has not been reconciled yet". Idempotent: a later run keeps the first completion
    /// time, because that is when the read switch became safe.
    /// </summary>
    public void MarkComplete(DateTimeOffset completedAt)
    {
        if (CompletedAt.HasValue)
            return;

        CompletedAt = completedAt;
        Touch();
    }
}
