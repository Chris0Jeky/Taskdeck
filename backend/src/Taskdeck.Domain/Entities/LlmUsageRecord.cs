using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Tracks per-request LLM token usage for quota enforcement and cost visibility.
/// </summary>
public class LlmUsageRecord : Entity
{
    public Guid UserId { get; private set; }
    public LlmSurface Surface { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }

    /// <summary>
    /// Lifecycle state. A directly-recorded usage row is <see cref="LlmUsageRecordStatus.Committed"/>;
    /// the atomic reservation flow (issue #1313) inserts a <see cref="LlmUsageRecordStatus.Reserved"/>
    /// row up front and later commits or releases it.
    /// </summary>
    public LlmUsageRecordStatus Status { get; private set; } = LlmUsageRecordStatus.Committed;

    /// <summary>
    /// Expiry instant for a <see cref="LlmUsageRecordStatus.Reserved"/> row. A reservation only counts
    /// toward quota while <c>ExpiresAt &gt; now</c>; a crashed process's stale reservation is ignored
    /// once past this instant and swept on the next reservation attempt. Null for committed rows.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    private LlmUsageRecord() : base() { }

    public LlmUsageRecord(
        Guid userId,
        LlmSurface surface,
        string provider,
        string model,
        int inputTokens,
        int outputTokens)
        : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (string.IsNullOrWhiteSpace(provider))
            throw new DomainException(ErrorCodes.ValidationError, "Provider cannot be empty");

        if (inputTokens < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Input tokens cannot be negative");

        if (outputTokens < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Output tokens cannot be negative");

        UserId = userId;
        Surface = surface;
        Provider = provider;
        Model = model ?? string.Empty;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        Status = LlmUsageRecordStatus.Committed;
        ExpiresAt = null;
    }

    /// <summary>
    /// Creates an in-flight quota reservation (issue #1313): a <see cref="LlmUsageRecordStatus.Reserved"/>
    /// row holding one request slot and an estimated token amount until finalized. The SQLite hot path
    /// inserts the equivalent row via a single conditional <c>INSERT ... SELECT ... WHERE</c> statement
    /// serialized by the database's writer lock (no explicit transaction); this factory backs the
    /// non-SQLite fallback and keeps the field shape in one place.
    /// </summary>
    public static LlmUsageRecord CreateReservation(
        Guid userId,
        LlmSurface surface,
        int estimatedTokens,
        DateTimeOffset expiresAt)
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (estimatedTokens < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Estimated tokens cannot be negative");

        return new LlmUsageRecord
        {
            UserId = userId,
            Surface = surface,
            Provider = ReservationProvider,
            Model = string.Empty,
            InputTokens = estimatedTokens,
            OutputTokens = 0,
            Status = LlmUsageRecordStatus.Reserved,
            ExpiresAt = expiresAt
        };
    }

    /// <summary>
    /// Finalizes a reservation with the actual token counts, clearing the expiry so the row counts as
    /// permanent committed usage. Idempotent-safe: a no-op if already committed.
    /// </summary>
    public void Commit(string provider, string model, int inputTokens, int outputTokens)
    {
        if (Status == LlmUsageRecordStatus.Committed)
            return;

        if (string.IsNullOrWhiteSpace(provider))
            throw new DomainException(ErrorCodes.ValidationError, "Provider cannot be empty");

        if (inputTokens < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Input tokens cannot be negative");

        if (outputTokens < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Output tokens cannot be negative");

        Provider = provider;
        Model = model ?? string.Empty;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        Status = LlmUsageRecordStatus.Committed;
        ExpiresAt = null;
        Touch();
    }

    /// <summary>Placeholder provider stamped on a reservation until it is committed with real values.</summary>
    public const string ReservationProvider = "reserved";

    public int TotalTokens => InputTokens + OutputTokens;
}
