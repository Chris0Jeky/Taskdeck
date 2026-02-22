using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.DTOs;

public record NotificationDto(
    Guid Id,
    Guid UserId,
    Guid? BoardId,
    NotificationType Type,
    NotificationCadence Cadence,
    string Title,
    string Message,
    string? SourceEntityType,
    Guid? SourceEntityId,
    bool IsRead,
    DateTimeOffset? ReadAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record NotificationQueryDto(
    bool UnreadOnly = false,
    Guid? BoardId = null,
    int Limit = 100
);

public record NotificationPreferenceDto(
    Guid UserId,
    bool InAppChannelEnabled,
    bool MentionImmediateEnabled,
    bool MentionDigestEnabled,
    bool AssignmentImmediateEnabled,
    bool AssignmentDigestEnabled,
    bool ProposalOutcomeImmediateEnabled,
    bool ProposalOutcomeDigestEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record UpdateNotificationPreferenceDto(
    bool InAppChannelEnabled,
    bool MentionImmediateEnabled,
    bool MentionDigestEnabled,
    bool AssignmentImmediateEnabled,
    bool AssignmentDigestEnabled,
    bool ProposalOutcomeImmediateEnabled,
    bool ProposalOutcomeDigestEnabled
);

public record CreateNotificationRequestDto(
    Guid UserId,
    NotificationType Type,
    string Title,
    string Message,
    Guid? BoardId = null,
    string? SourceEntityType = null,
    Guid? SourceEntityId = null,
    string? DeduplicationKey = null
);
