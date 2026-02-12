using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class ChatSession : Entity
{
    public string UserId { get; private set; }
    public string? BoardId { get; private set; }
    public string Title { get; private set; }
    public ChatSessionStatus Status { get; private set; }

    private readonly List<ChatMessage> _messages = new();
    public IReadOnlyList<ChatMessage> Messages => _messages.AsReadOnly();

    private ChatSession() { } // EF Core

    public ChatSession(
        string userId,
        string title,
        string? boardId = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException(ErrorCodes.ValidationError, "UserId cannot be empty");
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException(ErrorCodes.ValidationError, "Title cannot be empty");
        if (title.Length > 200)
            throw new DomainException(ErrorCodes.ValidationError, "Title cannot exceed 200 characters");

        UserId = userId;
        BoardId = boardId;
        Title = title;
        Status = ChatSessionStatus.Active;
    }

    public void UpdateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException(ErrorCodes.ValidationError, "Title cannot be empty");
        if (title.Length > 200)
            throw new DomainException(ErrorCodes.ValidationError, "Title cannot exceed 200 characters");

        Title = title;
        Touch();
    }

    public void Archive()
    {
        if (Status == ChatSessionStatus.Archived)
            throw new DomainException(ErrorCodes.InvalidOperation, "Session is already archived");

        Status = ChatSessionStatus.Archived;
        Touch();
    }

    public void Reactivate()
    {
        if (Status == ChatSessionStatus.Active)
            throw new DomainException(ErrorCodes.InvalidOperation, "Session is already active");

        Status = ChatSessionStatus.Active;
        Touch();
    }

    public void AddMessage(ChatMessage message)
    {
        if (Status == ChatSessionStatus.Archived)
            throw new DomainException(ErrorCodes.InvalidOperation, "Cannot add messages to archived session");

        _messages.Add(message);
        Touch();
    }
}

public enum ChatSessionStatus
{
    Active,
    Archived
}
