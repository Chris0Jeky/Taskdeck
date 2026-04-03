using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class ChatMessage : Entity
{
    private static readonly string[] ValidMessageTypes =
    {
        "text",
        "proposal-reference",
        "error",
        "status",
        "degraded"
    };

    private static readonly HashSet<string> ValidMessageTypeSet = new(ValidMessageTypes, StringComparer.Ordinal);

    public Guid SessionId { get; private set; }
    public ChatMessageRole Role { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string MessageType { get; private set; } = string.Empty;
    public Guid? ProposalId { get; private set; }
    public int? TokenUsage { get; private set; }
    public string? DegradedReason { get; private set; }
    public string? ToolCallMetadataJson { get; private set; }

    // Navigation
    public ChatSession Session { get; private set; } = null!;

    private ChatMessage() { } // EF Core

    public ChatMessage(
        Guid sessionId,
        ChatMessageRole role,
        string content,
        string messageType = "text",
        Guid? proposalId = null,
        int? tokenUsage = null,
        string? degradedReason = null)
    {
        if (sessionId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "SessionId cannot be empty");
        if (string.IsNullOrWhiteSpace(content))
            throw new DomainException(ErrorCodes.ValidationError, "Content cannot be empty");
        if (string.IsNullOrWhiteSpace(messageType))
            throw new DomainException(ErrorCodes.ValidationError, "MessageType cannot be empty");
        if (!ValidMessageTypeSet.Contains(messageType))
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"MessageType must be one of: {string.Join(", ", ValidMessageTypes)}");
        if (tokenUsage.HasValue && tokenUsage.Value < 0)
            throw new DomainException(ErrorCodes.ValidationError, "TokenUsage must be non-negative");

        SessionId = sessionId;
        Role = role;
        Content = content;
        MessageType = messageType;
        ProposalId = proposalId;
        TokenUsage = tokenUsage;
        DegradedReason = string.IsNullOrWhiteSpace(degradedReason) ? null : degradedReason.Trim();
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

    public void SetToolCallMetadataJson(string? metadataJson)
    {
        ToolCallMetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? null : metadataJson.Trim();
        Touch();
    }
}

public enum ChatMessageRole
{
    User,
    Assistant,
    System
}
