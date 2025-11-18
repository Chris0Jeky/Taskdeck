using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class Card : Entity
{
    private string _title = string.Empty;
    private readonly List<CardLabel> _cardLabels = new();

    public Guid BoardId { get; private set; }
    public Board Board { get; private set; } = null!;

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
    public string? BlockReason { get; private set; }
    public int Position { get; private set; }

    public IReadOnlyCollection<CardLabel> CardLabels => _cardLabels.AsReadOnly();

    private Card() : base() { }

    public Card(Guid boardId, Guid columnId, string title, string? description = null, DateTimeOffset? dueDate = null, int position = 0)
        : base()
    {
        BoardId = boardId;
        ColumnId = columnId;
        Title = title;
        Description = description ?? string.Empty;
        DueDate = dueDate;
        Position = position;
        IsBlocked = false;
        BlockReason = null;
    }

    public void Update(string? title = null, string? description = null, DateTimeOffset? dueDate = null)
    {
        if (title != null)
            Title = title;

        if (description != null)
            Description = description;

        // Allow explicitly setting to null
        if (dueDate.HasValue)
            DueDate = dueDate.Value;

        Touch();
    }

    public void SetPosition(int position)
    {
        if (position < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Position cannot be negative");

        Position = position;
        Touch();
    }

    public void MoveToColumn(Guid columnId, int position)
    {
        ColumnId = columnId;
        SetPosition(position);
    }

    public void Block(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException(ErrorCodes.ValidationError, "Block reason cannot be empty");

        IsBlocked = true;
        BlockReason = reason;
        Touch();
    }

    public void Unblock()
    {
        IsBlocked = false;
        BlockReason = null;
        Touch();
    }

    // Label management (called by application services)
    public void AddLabel(CardLabel cardLabel)
    {
        _cardLabels.Add(cardLabel);
    }

    public void RemoveLabel(CardLabel cardLabel)
    {
        _cardLabels.Remove(cardLabel);
    }

    public void ClearLabels()
    {
        _cardLabels.Clear();
    }
}
