using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>Responsibility only. This association never grants board authority.</summary>
public sealed class CardAssignment
{
    public Guid CardId { get; private set; }
    public Card Card { get; private set; } = null!;
    public Guid UserId { get; private set; }
    public User User { get; private set; } = null!;
    public DateTimeOffset AssignedAt { get; private set; }
    public Guid AssignedByUserId { get; private set; }

    private CardAssignment() { }

    public CardAssignment(Guid cardId, Guid userId, Guid assignedByUserId)
    {
        if (cardId == Guid.Empty || userId == Guid.Empty || assignedByUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Assignment identities cannot be empty.");
        CardId = cardId;
        UserId = userId;
        AssignedByUserId = assignedByUserId;
        AssignedAt = DateTimeOffset.UtcNow;
    }
}
