
namespace Taskdeck.Acceleration.V06;

public enum ConsentGrantState
{
    Active = 0,
    Revoked = 1,
    Expired = 2,
    Superseded = 3
}

public sealed record ProcessingConsentGrant(
    Guid Id,
    Guid OwnerUserId,
    string DestinationHost,
    string DataClass,
    string ProcessorFamily,
    int Version,
    DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt)
{
    public ConsentGrantState StateAt(DateTimeOffset now)
    {
        if (RevokedAt.HasValue && RevokedAt.Value <= now)
            return ConsentGrantState.Revoked;
        if (ExpiresAt.HasValue && ExpiresAt.Value <= now)
            return ConsentGrantState.Expired;
        return ConsentGrantState.Active;
    }

    public bool Covers(
        Guid ownerUserId,
        string destinationHost,
        string dataClass,
        string processorFamily,
        DateTimeOffset now) =>
        OwnerUserId == ownerUserId &&
        StateAt(now) == ConsentGrantState.Active &&
        StringComparer.OrdinalIgnoreCase.Equals(DestinationHost, destinationHost) &&
        StringComparer.Ordinal.Equals(DataClass, dataClass) &&
        StringComparer.Ordinal.Equals(ProcessorFamily, processorFamily);
}
