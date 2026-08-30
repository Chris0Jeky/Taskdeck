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
    string? DegradedReason = null,
    string? ToolCallMetadataJson = null
);

public record ChatProviderHealthDto(
    bool IsAvailable,
    string ProviderName,
    string? ErrorMessage,
    string? Model,
    bool IsMock,
    bool IsProbed = false,
    string VerificationStatus = "unverified",
    long? ProbeLatencyMs = null,
    // True when retired provider configuration inherited from the process environment was
    // ignored at startup (packaged desktop only, #2233). Value-blind: a flag, never the names
    // or values of the leftover variables.
    bool RetiredProviderConfigurationIgnored = false
);

public record CreateChatSessionDto(
    string Title,
    Guid? BoardId = null
);

public record SendChatMessageDto(
    string Content,
    bool RequestProposal = false
);
