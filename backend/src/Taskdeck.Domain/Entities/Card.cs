using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Domain.Entities;

public class Card : Entity
{
    private string _title = string.Empty;
    private readonly List<CardLabel> _cardLabels = new();
    private readonly List<CardAssignment> _assignments = new();
    public IReadOnlyCollection<CardAssignment> Assignments => _assignments.AsReadOnly();

    public bool ReplaceAssignments(IEnumerable<Guid> userIds, Guid actorUserId)
    {
        EnsureActive();
        var requested = userIds.ToHashSet();
        if (requested.Contains(Guid.Empty) || actorUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Assignment identities cannot be empty.");
        if (requested.SetEquals(_assignments.Select(a => a.UserId))) return false;
        _assignments.RemoveAll(a => !requested.Contains(a.UserId));
        foreach (var userId in requested.Except(_assignments.Select(a => a.UserId)))
            _assignments.Add(new CardAssignment(Id, userId, actorUserId));
        Touch();
        return true;
    }

    // Eligibility cleanup also applies to archived cards.
    public bool DetachAssignment(Guid userId)
    {
        if (_assignments.RemoveAll(a => a.UserId == userId) == 0) return false;
        Touch();
        return true;
    }

    public Guid BoardId { get; private set; }
    public Board Board { get; private set; } = null!;

    public Guid? ParentCardId { get; private set; }

    public Guid ColumnId { get; private set; }
    public Column Column { get; private set; } = null!;

    public string Title
    {
        get => _title;
        private set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException(ErrorCodes.ValidationError, "Card title cannot be empty");

            if (value.Length > 200)
                throw new DomainException(ErrorCodes.ValidationError, "Card title cannot exceed 200 characters");

            _title = value;
        }
    }

    public string Description { get; private set; } = string.Empty;
    public DateTimeOffset? DueDate { get; private set; }
    public bool IsBlocked { get; private set; }
    public bool IsArchived { get; private set; }
    public CardWorkItemType WorkItemType { get; private set; } = CardWorkItemType.Task;
    public string? BlockReason { get; private set; }
    public int Position { get; private set; }

    public IReadOnlyCollection<CardLabel> CardLabels => _cardLabels.AsReadOnly();

    private Card() : base() { }

    public Card(Guid boardId, Guid columnId, string title, string? description = null, DateTimeOffset? dueDate = null, int position = 0)
        : base()
    {
        Initialize(boardId, columnId, title, description, dueDate, position);
    }

    public Card(Guid cardId, Guid boardId, Guid columnId, string title, string? description = null, DateTimeOffset? dueDate = null, int position = 0)
        : base(cardId)
    {
        if (cardId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Card ID cannot be empty");

        Initialize(boardId, columnId, title, description, dueDate, position);
    }

    public void Update(string? title = null, string? description = null, DateTimeOffset? dueDate = null)
    {
        EnsureActive();
        if (title != null)
            Title = title;

        if (description != null)
            SetDescription(description);

        // Clearing is explicit via ClearDueDate so omitted values remain unchanged.
        if (dueDate.HasValue)
            DueDate = dueDate.Value;

        Touch();
    }

    public void ClearDueDate()
    {
        EnsureActive();
        DueDate = null;
        Touch();
    }

    public static CardWorkItemType ParseWorkItemType(string value) => value switch
    {
        "Task" => CardWorkItemType.Task,
        "Epic" => CardWorkItemType.Epic,
        "Spike" => CardWorkItemType.Spike,
        _ => throw new DomainException(ErrorCodes.ValidationError, "WorkItemType must be Task, Epic, or Spike")
    };

    public void SetWorkItemType(CardWorkItemType workItemType)
    {
        EnsureActive();
        if (!Enum.IsDefined(workItemType))
            throw new DomainException(ErrorCodes.ValidationError, "WorkItemType must be Task, Epic, or Spike");
        WorkItemType = workItemType;
        Touch();
    }

    private void SetDescription(string description)
    {
        if (description.Length > 2000)
            throw new DomainException(ErrorCodes.ValidationError, "Card description cannot exceed 2000 characters");

        Description = description;
    }

    public void SetPosition(int position)
    {
        EnsureActive();
        if (position < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Position cannot be negative");

        Position = position;
        Touch();
    }

    public void MoveToColumn(Guid columnId, int position)
    {
        EnsureActive();
        ColumnId = columnId;
        SetPosition(position);
    }

    public void Block(string reason)
    {
        EnsureActive();
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException(ErrorCodes.ValidationError, "Block reason cannot be empty");

        IsBlocked = true;
        BlockReason = reason;
        Touch();
    }

    public void Unblock()
    {
        EnsureActive();
        IsBlocked = false;
        BlockReason = null;
        Touch();
    }

    // Label management (called by application services)
    public void AddLabel(CardLabel cardLabel)
    {
        EnsureActive();
        if (_cardLabels.Any(cl => cl.LabelId == cardLabel.LabelId))
            throw new DomainException(ErrorCodes.ValidationError, "Label is already assigned to this card");

        _cardLabels.Add(cardLabel);
        Touch();
    }

    public void RemoveLabel(CardLabel cardLabel)
    {
        EnsureActive();
        _cardLabels.Remove(cardLabel);
        Touch();
    }

    public void ClearLabels()
    {
        EnsureActive();
        _cardLabels.Clear();
        Touch();
    }

    public void SetParent(Guid? parentCardId)
    {
        EnsureActive();
        if (parentCardId == Id || parentCardId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "A card cannot be its own parent or have an empty parent ID.");
        ParentCardId = parentCardId;
        Touch();
    }

    // Lifecycle detachment also covers archived children without restoring them.
    public void DetachParent()
    {
        ParentCardId = null;
        Touch();
    }

    public void Archive()
    {
        if (IsArchived)
            throw new DomainException(ErrorCodes.InvalidOperation, "Card is already archived. Refresh its history.");
        IsArchived = true;
        Touch();
    }

    public void Restore()
    {
        if (!IsArchived)
            throw new DomainException(ErrorCodes.InvalidOperation, "Card is already active. Refresh the board.");
        IsArchived = false;
        Touch();
    }

    private void EnsureActive()
    {
        if (IsArchived)
            throw new DomainException(ErrorCodes.InvalidOperation, "Card is archived. Restore the card before editing.");
    }

    private void Initialize(
        Guid boardId,
        Guid columnId,
        string title,
        string? description,
        DateTimeOffset? dueDate,
        int position)
    {
        BoardId = boardId;
        ColumnId = columnId;
        Title = title;
        SetDescription(description ?? string.Empty);
        DueDate = dueDate;
        Position = position;
        IsBlocked = false;
        BlockReason = null;
    }
}
