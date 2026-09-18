using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.DTOs;

public sealed record WorkspaceAttentionDto(bool Enabled, long Revision, int DailyLimit, int MinimumSpacingMinutes, WorkspaceAttentionWindow? Window = null);
public sealed record SaveWorkspaceAttentionDto(long ExpectedRevision, bool Enabled, bool UpdateWindow = false, WorkspaceAttentionWindow? Window = null);
public sealed record ClaimWorkspaceAttentionDto(Guid BoardId);
public sealed record WorkspaceAttentionReminderDto(Guid BoardId, Guid InsightId);
