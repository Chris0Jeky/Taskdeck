namespace Taskdeck.Application.DTOs;

public sealed record WorkspaceAttentionDto(bool Enabled, long Revision, int DailyLimit, int MinimumSpacingMinutes);
public sealed record SaveWorkspaceAttentionDto(long ExpectedRevision, bool Enabled);
public sealed record ClaimWorkspaceAttentionDto(Guid BoardId);
public sealed record WorkspaceAttentionReminderDto(Guid BoardId, Guid InsightId);
