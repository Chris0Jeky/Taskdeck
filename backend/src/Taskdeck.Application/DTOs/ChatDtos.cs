using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.DTOs;

public record ChatSessionDto(
    Guid Id,
    Guid UserId,
    Guid? BoardId,
    string Title,
    ChatSessionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<ChatMessageDto> RecentMessages
);

public record ChatMessageDto(
    Guid Id,
    Guid SessionId,
    ChatMessageRole Role,
    string Content,
    string MessageType,
    Guid? ProposalId,
    int? TokenUsage,
    DateTimeOffset CreatedAt,
    string? DegradedReason = null
);

public record ChatProviderHealthDto(
    bool IsAvailable,
    string ProviderName,
    string? ErrorMessage,
    string? Model,
    bool IsMock,
    bool IsProbed = false
);

public record CreateChatSessionDto(
    string Title,
    Guid? BoardId = null
);

public record SendChatMessageDto(
    string Content,
    bool RequestProposal = false
);
