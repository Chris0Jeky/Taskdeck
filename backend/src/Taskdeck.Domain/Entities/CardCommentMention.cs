using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class CardCommentMention : Entity
{
    private const int MaxMentionedUsernameLength = 50;

    public Guid CardCommentId { get; private set; }
    public Guid MentionedUserId { get; private set; }
    public string MentionedUsername { get; private set; } = string.Empty;

    public CardComment CardComment { get; private set; } = null!;
    public User MentionedUser { get; private set; } = null!;

    private CardCommentMention() : base()
    {
    }

    public CardCommentMention(
        Guid cardCommentId,
        Guid mentionedUserId,
        string mentionedUsername)
        : base()
    {
        if (cardCommentId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Card comment ID cannot be empty");

        if (mentionedUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Mentioned user ID cannot be empty");

        if (string.IsNullOrWhiteSpace(mentionedUsername))
            throw new DomainException(ErrorCodes.ValidationError, "Mentioned username cannot be empty");

        if (mentionedUsername.Length > MaxMentionedUsernameLength)
        {
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Mentioned username cannot exceed {MaxMentionedUsernameLength} characters");
        }

        CardCommentId = cardCommentId;
        MentionedUserId = mentionedUserId;
        MentionedUsername = mentionedUsername;
    }
}
