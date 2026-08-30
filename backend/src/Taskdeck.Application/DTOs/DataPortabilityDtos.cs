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
    UserDataExportNotificationPreferencesDto? NotificationPreferences,
    IReadOnlyList<UserDataExportProposalFeedbackDto> ProposalFeedback,
    IReadOnlyList<UserDataExportArtefactDto>? Artefacts = null,
    IReadOnlyList<UserDataExportTranscriptDto>? Transcripts = null);

/// <summary>
/// A portable normalized transcript. <see cref="Text"/> remains the only
/// transcript-text payload; source artefacts are referenced by identity only.
/// </summary>
public record UserDataExportTranscriptDto(
    Guid Id,
    Guid? BoardId,
    string CaptureSource,
    string Text,
    IReadOnlyList<UserDataExportTranscriptSegmentDto> Segments,
    Guid? CreatedFromCaptureId,
    Guid? SourceArtefactId,
    DateTimeOffset CreatedAt);

public record UserDataExportTranscriptSegmentDto(
    int StartLine,
    int EndLine,
    string? Speaker,
    long? TimestampMilliseconds);

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
    DateTimeOffset CreatedAt,
    Guid? BoardId = null,
    CaptureProvenanceV1? Provenance = null,
    UserDataExportCaptureDispositionDto? Disposition = null,
    // ADR-0065 / CF-01 (#2255): the durable aggregate behind this capture, when one exists. Added
    // as an optional member so a reader of the previous shape keeps working, and null on a host
    // whose ContextFabric:DualWriteCaptures has never been on.
    UserDataExportDurableCaptureDto? DurableCapture = null);

/// <summary>
/// The durable <c>Capture</c> row and its immutable <c>SourceAsset</c>s, exported so a portability
/// package carries the capture's own record - its three state axes, its provenance dimensions and
/// the exact material the user gave Taskdeck - and not only the job row that processed it.
/// </summary>
public record UserDataExportDurableCaptureDto(
    string Disposition,
    string ProcessingSummary,
    string ActionState,
    string Timeline,
    string ProducerKind,
    string RequestedIntent,
    string? EffectiveIntent,
    string PrimaryModality,
    string OriginAdapter,
    string LegacySourceSnapshot,
    DateTimeOffset CapturedAtServer,
    DateTimeOffset? CapturedAtClient,
    string? UserTitle,
    string? UserNote,
    IReadOnlyList<UserDataExportSourceAssetDto> SourceAssets);

/// <summary>
/// One immutable source of a capture. <c>Text</c> carries the stored bytes for an inline text asset
/// (the user's own words - portability means exporting them); binary assets export their locator and
/// digest, and their bytes travel in the artefacts section.
/// </summary>
public record UserDataExportSourceAssetDto(
    Guid Id,
    int Ordinal,
    string Modality,
    string MediaType,
    string ContentHash,
    long ByteSize,
    string StorageKind,
    string? ExternalReference,
    string? OriginalName,
    Guid? SupersedesAssetId,
    Guid? SupersededByAssetId,
    string? Text);

public record UserDataExportCaptureDispositionDto(
    string Kind,
    DateTimeOffset At,
    Guid ByUserId,
    Guid? BoardId = null);

public record UserDataExportProposalDto(
    Guid Id,
    string Status,
    string? Summary,
    Guid? BoardId,
    DateTimeOffset CreatedAt,
    // The active snooze deadline (#1245 Codex review): a deferred proposal is hidden from the
    // review queue until this time, so the export must carry it to represent the proposal's
    // current state. Null when the proposal is not snoozed.
    DateTime? DeferredUntil = null);

public record UserDataExportChatSessionDto(
    Guid Id,
    string Status,
    int MessageCount,
    DateTimeOffset CreatedAt);

/// <summary>
/// A content-free quality-feedback signal the user reported on a proposal (reason category only,
/// no free text). Included for data portability (#1245 review).
/// </summary>
public record UserDataExportProposalFeedbackDto(
    Guid ProposalId,
    string Reason,
    DateTimeOffset ReportedAt);

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
    int PreferencesDeleted,
    int ArtefactsDeleted = 0,
    int TranscriptsDeleted = 0,
    int DurableCapturesDeleted = 0);
