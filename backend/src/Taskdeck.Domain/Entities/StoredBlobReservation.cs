using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>Temporary quota claim for a source upload while bytes arrive outside a database transaction.</summary>
public sealed class StoredBlobReservation
{
    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public CaptureModality Modality { get; private set; }
    public long ByteSize { get; private set; }
    public string? ReferrerKind { get; private set; }
    public Guid? ReferrerId { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    private StoredBlobReservation() { }

    public StoredBlobReservation(
        Guid ownerUserId,
        CaptureModality modality,
        long byteSize,
        string? referrerKind,
        Guid? referrerId,
        DateTime expiresAtUtc)
    {
        if (ownerUserId == Guid.Empty || !Enum.IsDefined(modality) || byteSize <= 0 ||
            referrerKind?.Length > 100 || referrerId == Guid.Empty ||
            expiresAtUtc.Kind != DateTimeKind.Utc || expiresAtUtc <= DateTime.UtcNow)
            throw new DomainException(ErrorCodes.ValidationError, "Invalid source upload reservation.");

        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        Modality = modality;
        ByteSize = byteSize;
        ReferrerKind = referrerKind;
        ReferrerId = referrerId;
        ExpiresAtUtc = expiresAtUtc;
    }
}
