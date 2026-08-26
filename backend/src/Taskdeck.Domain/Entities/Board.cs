using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class Board : Entity
{
    private string _name = string.Empty;
    private readonly List<Column> _columns = new();
    private readonly List<Card> _cards = new();
    private readonly List<Label> _labels = new();
    private readonly List<BoardAccess> _boardAccesses = new();

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
    public Guid? OwnerId { get; private set; }
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

    public IReadOnlyCollection<Column> Columns => _columns.AsReadOnly();
    public IReadOnlyCollection<Card> Cards => _cards.AsReadOnly();
    public IReadOnlyCollection<Label> Labels => _labels.AsReadOnly();
    public IReadOnlyCollection<BoardAccess> BoardAccesses => _boardAccesses.AsReadOnly();

    private Board() : base() { }

    public Board(string name, string? description = null, Guid? ownerId = null) : base()
    {
        if (ownerId.HasValue && ownerId.Value == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Owner ID cannot be empty");

        Name = name;
        SetDescription(description);
        IsArchived = false;
        OwnerId = ownerId;
    }

    public void Update(string? name = null, string? description = null)
    {
        if (name != null)
            Name = name;

        if (description != null)
            SetDescription(description);

        TouchAndAdvanceConcurrencyToken();
    }

    private void SetDescription(string? description)
    {
        if (description != null && description.Length > 1000)
            throw new DomainException(ErrorCodes.ValidationError, "Board description cannot exceed 1000 characters");

        Description = description;
    }

    public void Archive()
    {
        IsArchived = true;
        TouchAndAdvanceConcurrencyToken();
    }

    public void Unarchive()
    {
        IsArchived = false;
        TouchAndAdvanceConcurrencyToken();
    }

    public void TransferOwnership(Guid newOwnerId)
    {
        if (newOwnerId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "New owner ID cannot be empty");

        OwnerId = newOwnerId;
        TouchAndAdvanceConcurrencyToken();
    }

    /// <summary>
    /// Records a card mutation without advancing the board concurrency token. Touching the board
    /// makes EF issue a conditional update using the current token, so a board mutation that
    /// advanced the token after the archived-state check still rejects the card write. Independent
    /// card writes keep the same token and retain their established success semantics.
    /// </summary>
    public void RecordCardMutation()
    {
        Touch();
    }

    private void TouchAndAdvanceConcurrencyToken()
    {
        Touch();
        ConcurrencyToken = Guid.NewGuid();
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

    internal void AddBoardAccess(BoardAccess boardAccess)
    {
        _boardAccesses.Add(boardAccess);
    }

    internal void RemoveBoardAccess(BoardAccess boardAccess)
    {
        _boardAccesses.Remove(boardAccess);
    }
}
