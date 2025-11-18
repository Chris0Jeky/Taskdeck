using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class Board : Entity
{
    private string _name = string.Empty;
    private readonly List<Column> _columns = new();
    private readonly List<Card> _cards = new();
    private readonly List<Label> _labels = new();

    public string Name
    {
        get => _name;
        private set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException(ErrorCodes.ValidationError, "Board name cannot be empty");

            if (value.Length > 100)
                throw new DomainException(ErrorCodes.ValidationError, "Board name cannot exceed 100 characters");

            _name = value;
        }
    }

    public string? Description { get; private set; }
    public bool IsArchived { get; private set; }

    public IReadOnlyCollection<Column> Columns => _columns.AsReadOnly();
    public IReadOnlyCollection<Card> Cards => _cards.AsReadOnly();
    public IReadOnlyCollection<Label> Labels => _labels.AsReadOnly();

    private Board() : base() { }

    public Board(string name, string? description = null) : base()
    {
        Name = name;
        Description = description;
        IsArchived = false;
    }

    public void Update(string? name = null, string? description = null)
    {
        if (name != null)
            Name = name;

        if (description != null)
            Description = description;

        Touch();
    }

    public void Archive()
    {
        IsArchived = true;
        Touch();
    }

    public void Unarchive()
    {
        IsArchived = false;
        Touch();
    }

    // Navigation properties management (called by infrastructure)
    internal void AddColumn(Column column)
    {
        _columns.Add(column);
    }

    internal void RemoveColumn(Column column)
    {
        _columns.Remove(column);
    }

    internal void AddCard(Card card)
    {
        _cards.Add(card);
    }

    internal void RemoveCard(Card card)
    {
        _cards.Remove(card);
    }

    internal void AddLabel(Label label)
    {
        _labels.Add(label);
    }

    internal void RemoveLabel(Label label)
    {
        _labels.Remove(label);
    }
}
