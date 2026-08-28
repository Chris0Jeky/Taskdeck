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

    /// <summary>
    /// Monotonic guard marker advanced by <see cref="RecordDependentMutation"/>. It is deliberately
    /// non-user-visible: no DTO, API contract, or query reads it. Its only job is to guarantee the
    /// board row joins a dependent write's UPDATE statement so the concurrency-token predicate runs.
    /// It is NOT a reliable mutation count — two writers that read the same value both persist
    /// value + 1, which is harmless because nothing compares it against an expected value.
    /// </summary>
    public long CardMutationMarker { get; private set; }

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
    /// Records a card mutation by advancing <see cref="CardMutationMarker"/> — without advancing the
    /// board concurrency token and without re-stamping <see cref="Entity.UpdatedAt"/>. Changing the
    /// marker makes EF issue a conditional update using the token the write read, so a board mutation
    /// that advanced the token after the archived-state check still rejects the card write.
    /// Independent card writes keep the same token and retain their established success semantics.
    ///
    /// The marker exists rather than a re-stamp of <c>UpdatedAt</c> for two reasons. It keeps the
    /// board's user-visible timestamp meaning "board metadata last changed", so a card write cannot
    /// make an already-cached board list disagree with the database (`#2115`). And it is
    /// deterministic: re-stamping <c>UpdatedAt</c> with the current time is a no-op inside one clock
    /// tick, which leaves the entity Unchanged, emits no board UPDATE, and silently drops the token
    /// predicate (`#2123`). An incremented marker always differs from the value that was read.
    /// </summary>
    /// <remarks>
    /// The historic marker name is card-specific, but this seam protects any board-dependent write.
    /// </remarks>
    public void RecordDependentMutation()
    {
        CardMutationMarker++;
    }

    /// <summary>
    /// Preserves the card-write vocabulary while sharing the board-level dependent-write marker.
    /// </summary>
    public void RecordCardMutation() => RecordDependentMutation();

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
