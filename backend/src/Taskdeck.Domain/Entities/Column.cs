using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class Column : Entity
{
    private string _name = string.Empty;
    private readonly List<Card> _cards = new();

    public Guid BoardId { get; private set; }
    public Board Board { get; private set; } = null!;

    public string Name
    {
        get => _name;
        private set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException(ErrorCodes.ValidationError, "Column name cannot be empty");

            if (value.Length > 50)
                throw new DomainException(ErrorCodes.ValidationError, "Column name cannot exceed 50 characters");

            _name = value;
        }
    }

    public int Position { get; private set; }
    public int? WipLimit { get; private set; }

    public IReadOnlyCollection<Card> Cards => _cards.AsReadOnly();

    private Column() : base() { }

    public Column(Guid boardId, string name, int position, int? wipLimit = null) : base()
    {
        BoardId = boardId;
        Name = name;
        Position = position;
        SetWipLimit(wipLimit);
    }

    public void Update(string? name = null, int? wipLimit = null, int? position = null)
    {
        if (name != null)
            Name = name;

        if (wipLimit.HasValue || wipLimit == null)
            SetWipLimit(wipLimit);

        if (position.HasValue)
            Position = position.Value;

        Touch();
    }

    public void SetPosition(int position)
    {
        if (position < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Position cannot be negative");

        Position = position;
        Touch();
    }

    public void SetWipLimit(int? wipLimit)
    {
        if (wipLimit.HasValue && wipLimit.Value <= 0)
            throw new DomainException(ErrorCodes.ValidationError, "WIP limit must be greater than 0");

        WipLimit = wipLimit;
        Touch();
    }

    public bool IsWipLimitExceeded()
    {
        if (!WipLimit.HasValue)
            return false;

        var activeCardCount = _cards.Count;
        return activeCardCount > WipLimit.Value;
    }

    public bool WouldExceedWipLimitIfAdded()
    {
        if (!WipLimit.HasValue)
            return false;

        var activeCardCount = _cards.Count;
        return activeCardCount >= WipLimit.Value;
    }

    // Navigation properties management
    internal void AddCard(Card card)
    {
        _cards.Add(card);
    }

    internal void RemoveCard(Card card)
    {
        _cards.Remove(card);
    }
}
