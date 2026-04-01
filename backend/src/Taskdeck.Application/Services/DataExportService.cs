using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Exports all user data in a structured, versioned format for GDPR-style data portability.
/// All queries are scoped strictly to the requesting user's data.
/// </summary>
public class DataExportService : IDataExportService
{
    private const string ExportVersion = "1.0";
    private readonly IUnitOfWork _unitOfWork;
    private readonly IHistoryService _historyService;

    public DataExportService(IUnitOfWork unitOfWork, IHistoryService historyService)
    {
        _unitOfWork = unitOfWork;
        _historyService = historyService;
    }

    public async Task<Result<UserDataExportDto>> ExportUserDataAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<UserDataExportDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        var user = await _unitOfWork.Users.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return Result.Failure<UserDataExportDto>(ErrorCodes.NotFound, "User not found");

        try
        {
            // Gather all user-scoped data in parallel where safe
            var boardAccessesTask = _unitOfWork.BoardAccesses.GetByUserIdAsync(userId, cancellationToken);
            var notificationsTask = _unitOfWork.Notifications.GetByUserIdAsync(userId, limit: 10000, cancellationToken: cancellationToken);
            var capturesTask = _unitOfWork.LlmQueue.GetByUserAsync(userId, cancellationToken);
            var proposalsTask = _unitOfWork.AutomationProposals.GetByUserIdAsync(userId, limit: 10000, cancellationToken: cancellationToken);
            var chatSessionsTask = _unitOfWork.ChatSessions.GetByUserIdAsync(userId, limit: 10000, cancellationToken: cancellationToken);
            var auditLogsTask = _unitOfWork.AuditLogs.GetByUserAsync(userId, limit: 10000, cancellationToken: cancellationToken);
            var preferencesTask = _unitOfWork.UserPreferences.GetByUserIdAsync(userId, cancellationToken);
            var notificationPrefsTask = _unitOfWork.NotificationPreferences.GetByUserIdAsync(userId, cancellationToken);

            await Task.WhenAll(
                boardAccessesTask, notificationsTask, capturesTask,
                proposalsTask, chatSessionsTask, auditLogsTask,
                preferencesTask, notificationPrefsTask);

            var boardAccesses = await boardAccessesTask;
            var notifications = await notificationsTask;
            var captures = await capturesTask;
            var proposals = await proposalsTask;
            var chatSessions = await chatSessionsTask;
            var auditLogs = await auditLogsTask;
            var preferences = await preferencesTask;
            var notificationPrefs = await notificationPrefsTask;

            // Resolve board names for accessible boards
            var boardIds = boardAccesses.Select(ba => ba.BoardId).Distinct().ToList();
            var boards = boardIds.Count > 0
                ? await _unitOfWork.Boards.GetByIdsAsync(boardIds, cancellationToken)
                : Enumerable.Empty<Domain.Entities.Board>();
            var boardLookup = boards.ToDictionary(b => b.Id);

            // Build export DTOs
            var exportBoards = boardAccesses.Select(ba =>
            {
                boardLookup.TryGetValue(ba.BoardId, out var board);
                return new UserDataExportBoardDto(
                    ba.BoardId,
                    board?.Name ?? "[deleted]",
                    board?.Description,
                    ba.Role.ToString(),
                    ba.Role == UserRole.Owner,
                    ba.CreatedAt);
            }).ToList();

            var exportNotifications = notifications.Select(n => new UserDataExportNotificationDto(
                n.Id,
                n.Type.ToString(),
                n.Title,
                n.Message,
                n.IsRead,
                n.CreatedAt)).ToList();

            var exportCaptures = captures.Select(c => new UserDataExportCaptureDto(
                c.Id,
                c.Status.ToString(),
                c.RequestType,
                c.CreatedAt)).ToList();

            var exportProposals = proposals.Select(p => new UserDataExportProposalDto(
                p.Id,
                p.Status.ToString(),
                p.Summary,
                p.BoardId,
                p.CreatedAt)).ToList();

            // For chat sessions, count messages per session
            var exportChatSessions = new List<UserDataExportChatSessionDto>();
            foreach (var session in chatSessions)
            {
                var messages = await _unitOfWork.ChatMessages.GetBySessionIdAsync(session.Id, limit: 10000, cancellationToken: cancellationToken);
                exportChatSessions.Add(new UserDataExportChatSessionDto(
                    session.Id,
                    session.Status.ToString(),
                    messages.Count(),
                    session.CreatedAt));
            }

            var exportAuditEntries = auditLogs.Select(a => new UserDataExportAuditEntryDto(
                a.Id,
                a.EntityType,
                a.EntityId,
                a.Action.ToString(),
                a.Timestamp)).ToList();

            var exportPreferences = preferences is not null
                ? new UserDataExportPreferencesDto(
                    preferences.WorkspaceMode.ToString(),
                    preferences.CreatedAt)
                : null;

            var exportNotificationPrefs = notificationPrefs is not null
                ? new UserDataExportNotificationPreferencesDto(
                    notificationPrefs.InAppChannelEnabled,
                    notificationPrefs.MentionImmediateEnabled,
                    notificationPrefs.AssignmentImmediateEnabled,
                    notificationPrefs.ProposalOutcomeImmediateEnabled)
                : null;

            var profile = new UserDataExportProfileDto(
                user.Username,
                user.Email,
                user.IsActive,
                user.DefaultRole.ToString(),
                user.CreatedAt);

            var content = new UserDataExportContentDto(
                exportBoards,
                exportNotifications,
                exportCaptures,
                exportProposals,
                exportChatSessions,
                exportAuditEntries,
                exportPreferences,
                exportNotificationPrefs);

            var export = new UserDataExportDto(
                ExportVersion,
                DateTimeOffset.UtcNow,
                userId,
                profile,
                content);

            // Log the export action (non-sensitive — no user data in the audit entry)
            await _historyService.LogActionAsync(
                "User", userId, AuditAction.DataExported, userId,
                "User data export requested");

            return Result.Success(export);
        }
        catch (Exception ex)
        {
            return Result.Failure<UserDataExportDto>(
                ErrorCodes.UnexpectedError,
                $"Failed to export user data: {ex.Message}");
        }
    }
}
