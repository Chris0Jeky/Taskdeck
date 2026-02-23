using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class CardComment : Entity
{
    private const int MaxContentLength = 4000;
    private const string DeletedMarker = "[deleted]";

    private string _content = string.Empty;
    private readonly List<CardCommentMention> _mentions = new();

    public Guid CardId { get; private set; }
    public Guid BoardId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public Guid? ParentCommentId { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset? EditedAt { get; private set; }

    public string Content
    {
        get => _content;
        private set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new DomainException(ErrorCodes.ValidationError, "Comment content cannot be empty");

            if (value.Length > MaxContentLength)
                throw new DomainException(
                    ErrorCodes.ValidationError,
                    $"Comment content cannot exceed {MaxContentLength} characters");

            _content = value;
        }
    }

    public Card Card { get; private set; } = null!;
    public User AuthorUser { get; private set; } = null!;
    public CardComment? ParentComment { get; private set; }
    public ICollection<CardComment> Replies { get; private set; } = new List<CardComment>();

    public IReadOnlyCollection<CardCommentMention> Mentions => _mentions.AsReadOnly();

    private CardComment() : base()
    {
    }

    public CardComment(
        Guid cardId,
        Guid boardId,
        Guid authorUserId,
        string content,
        Guid? parentCommentId = null)
        : base()
    {
        if (cardId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Card ID cannot be empty");

        if (boardId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Board ID cannot be empty");

        if (authorUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Author user ID cannot be empty");

        if (parentCommentId.HasValue && parentCommentId.Value == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Parent comment ID cannot be empty");

        CardId = cardId;
        BoardId = boardId;
        AuthorUserId = authorUserId;
        ParentCommentId = parentCommentId;
        IsDeleted = false;
        DeletedAt = null;
        EditedAt = null;
        Content = content;
    }

    public void UpdateContent(string content)
    {
        if (IsDeleted)
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                "Deleted comments cannot be edited");
        }

        Content = content;
        EditedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void SoftDelete()
    {
        if (IsDeleted)
            return;

        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        EditedAt = DeletedAt;
        _content = DeletedMarker;
        _mentions.Clear();
        Touch();
    }

    public void ReplaceMentions(IEnumerable<(Guid userId, string username)> mentions)
    {
        _mentions.Clear();

        foreach (var mention in mentions
                     .Where(m => m.userId != Guid.Empty && !string.IsNullOrWhiteSpace(m.username))
                     .DistinctBy(m => m.userId))
        {
            _mentions.Add(new CardCommentMention(Id, mention.userId, mention.username));
        }

        Touch();
    }
}
