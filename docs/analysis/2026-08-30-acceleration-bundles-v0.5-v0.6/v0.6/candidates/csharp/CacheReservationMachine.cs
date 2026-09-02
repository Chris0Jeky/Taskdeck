
namespace Taskdeck.Acceleration.V06;

public enum CacheReservationState
{
    Reserved = 0,
    Committed = 1,
    Released = 2,
    Expired = 3
}

public sealed class CacheReservationMachine
{
    public Guid Id { get; }
    public string KeyDigest { get; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public CacheReservationState State { get; private set; }
    public Guid? RepresentationId { get; private set; }

    public CacheReservationMachine(Guid id, string keyDigest, DateTimeOffset expiresAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("id");
        if (string.IsNullOrWhiteSpace(keyDigest)) throw new ArgumentException("keyDigest");
        Id = id;
        KeyDigest = keyDigest;
        ExpiresAt = expiresAt;
        State = CacheReservationState.Reserved;
    }

    public bool TryCommit(Guid representationId, DateTimeOffset now)
    {
        if (representationId == Guid.Empty) return false;
        ExpireIfNeeded(now);
        if (State == CacheReservationState.Committed)
            return RepresentationId == representationId;
        if (State != CacheReservationState.Reserved) return false;
        RepresentationId = representationId;
        State = CacheReservationState.Committed;
        return true;
    }

    public bool TryRelease(DateTimeOffset now)
    {
        ExpireIfNeeded(now);
        if (State == CacheReservationState.Released) return true;
        if (State != CacheReservationState.Reserved) return false;
        State = CacheReservationState.Released;
        return true;
    }

    private void ExpireIfNeeded(DateTimeOffset now)
    {
        if (State == CacheReservationState.Reserved && ExpiresAt <= now)
            State = CacheReservationState.Expired;
    }
}
