namespace Taskdeck.Application.DTOs;

/// <summary>
/// Versioned export package containing all user data for GDPR-style data portability.
/// </summary>
public record UserDataExportDto(
    string Version,
    DateTimeOffset ExportedAt,
    Guid UserId,
    UserDataExportProfileDto Profile,
    UserDataExportContentDto Data);

public record UserDataExportProfileDto(
    string Username,
    string Email,
    bool IsActive,
    string DefaultRole,
    DateTimeOffset CreatedAt);

public record UserDataExportContentDto(
    IReadOnlyList<UserDataExportBoardDto> Boards,
    IReadOnlyList<UserDataExportNotificationDto> Notifications,
    IReadOnlyList<UserDataExportCaptureDto> CaptureItems,
    IReadOnlyList<UserDataExportProposalDto> Proposals,
    IReadOnlyList<UserDataExportChatSessionDto> ChatSessions,
    IReadOnlyList<UserDataExportAuditEntryDto> AuditTrail,
    UserDataExportPreferencesDto? Preferences,
    UserDataExportNotificationPreferencesDto? NotificationPreferences);

public record UserDataExportBoardDto(
    Guid BoardId,
    string Name,
    string? Description,
    string Role,
    bool IsOwner,
    DateTimeOffset CreatedAt);

public record UserDataExportNotificationDto(
    Guid Id,
    string Type,
    string Title,
    string Message,
    bool IsRead,
    DateTimeOffset CreatedAt);

public record UserDataExportCaptureDto(
    Guid Id,
    string Status,
    string? RequestType,
    DateTimeOffset CreatedAt);

public record UserDataExportProposalDto(
    Guid Id,
    string Status,
    string? Summary,
    Guid? BoardId,
    DateTimeOffset CreatedAt);

public record UserDataExportChatSessionDto(
    Guid Id,
    string Status,
    int MessageCount,
    DateTimeOffset CreatedAt);

public record UserDataExportAuditEntryDto(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string Action,
    DateTimeOffset Timestamp);

public record UserDataExportPreferencesDto(
    string WorkspaceMode,
    DateTimeOffset CreatedAt);

public record UserDataExportNotificationPreferencesDto(
    bool InAppChannelEnabled,
    bool MentionImmediateEnabled,
    bool AssignmentImmediateEnabled,
    bool ProposalOutcomeImmediateEnabled);

/// <summary>
/// Request to confirm account deletion. Requires the user's current password
/// and an explicit confirmation phrase for irreversible action safeguard.
/// </summary>
public record AccountDeletionRequest(
    string CurrentPassword,
    string ConfirmationPhrase);

/// <summary>
/// Result of an account deletion/anonymization operation.
/// </summary>
public record AccountDeletionResultDto(
    bool Success,
    string Message,
    int AuditLogsAnonymized,
    int NotificationsDeleted,
    int CaptureItemsDeleted,
    int ChatSessionsAnonymized,
    int ExternalLoginsDeleted,
    int PreferencesDeleted);
