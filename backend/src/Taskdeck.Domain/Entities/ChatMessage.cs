using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class ChatMessage : Entity
{
    public Guid SessionId { get; private set; }
    public ChatMessageRole Role { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string MessageType { get; private set; } = string.Empty;
    public Guid? ProposalId { get; private set; }
    public int? TokenUsage { get; private set; }

    // Navigation
    public ChatSession Session { get; private set; } = null!;

    private ChatMessage() { } // EF Core

    public ChatMessage(
        Guid sessionId,
        ChatMessageRole role,
        string content,
        string messageType = "text",
        Guid? proposalId = null,
        int? tokenUsage = null)
    {
        if (sessionId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "SessionId cannot be empty");
        if (string.IsNullOrWhiteSpace(content))
            throw new DomainException(ErrorCodes.ValidationError, "Content cannot be empty");
        if (string.IsNullOrWhiteSpace(messageType))
            throw new DomainException(ErrorCodes.ValidationError, "MessageType cannot be empty");
        if (messageType != "text" && messageType != "proposal-reference" && messageType != "error" && messageType != "status")
            throw new DomainException(ErrorCodes.ValidationError, "MessageType must be 'text', 'proposal-reference', 'error', or 'status'");
        if (tokenUsage.HasValue && tokenUsage.Value < 0)
            throw new DomainException(ErrorCodes.ValidationError, "TokenUsage must be non-negative");

        SessionId = sessionId;
        Role = role;
        Content = content;
        MessageType = messageType;
        ProposalId = proposalId;
        TokenUsage = tokenUsage;
    }

    public void SetTokenUsage(int tokenUsage)
    {
        if (tokenUsage < 0)
            throw new DomainException(ErrorCodes.ValidationError, "TokenUsage must be non-negative");

        TokenUsage = tokenUsage;
        Touch();
    }

    public void SetProposalId(Guid proposalId)
    {
        if (proposalId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "ProposalId cannot be empty");

        ProposalId = proposalId;
        Touch();
    }
}

public enum ChatMessageRole
{
    User,
    Assistant,
    System
}
