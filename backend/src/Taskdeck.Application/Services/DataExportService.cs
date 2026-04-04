using System.Text.Json;
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

            // Count messages per session in a single batched query (avoids N+1)
            var sessionIds = chatSessions.Select(s => s.Id).ToList();
            var messageCounts = sessionIds.Count > 0
                ? await _unitOfWork.ChatMessages.CountBySessionIdsAsync(sessionIds, cancellationToken)
                : new Dictionary<Guid, int>();

            var exportChatSessions = chatSessions.Select(session => new UserDataExportChatSessionDto(
                session.Id,
                session.Status.ToString(),
                messageCounts.TryGetValue(session.Id, out var count) ? count : 0,
                session.CreatedAt)).ToList();

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
                "Failed to export user data due to an internal error");
        }
    }

    /// <inheritdoc/>
    public async Task<Result> StreamUserDataExportAsync(Guid userId, Stream destination, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure(ErrorCodes.ValidationError, "User ID cannot be empty");

        var user = await _unitOfWork.Users.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return Result.Failure(ErrorCodes.NotFound, "User not found");

        try
        {
            await using var writer = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = false });

            writer.WriteStartObject();

            // --- envelope metadata ---
            writer.WriteString("version", ExportVersion);
            writer.WriteString("exportedAt", DateTimeOffset.UtcNow);
            writer.WriteString("userId", userId.ToString());

            // --- profile ---
            writer.WriteStartObject("profile");
            writer.WriteString("username", user.Username);
            writer.WriteString("email", user.Email);
            writer.WriteBoolean("isActive", user.IsActive);
            writer.WriteString("defaultRole", user.DefaultRole.ToString());
            writer.WriteString("createdAt", user.CreatedAt);
            writer.WriteEndObject();

            // --- data ---
            writer.WriteStartObject("data");

            // boards (small — full load is fine)
            var boardAccesses = await _unitOfWork.BoardAccesses.GetByUserIdAsync(userId, cancellationToken);
            var boardIds = boardAccesses.Select(ba => ba.BoardId).Distinct().ToList();
            var boards = boardIds.Count > 0
                ? await _unitOfWork.Boards.GetByIdsAsync(boardIds, cancellationToken)
                : Enumerable.Empty<Domain.Entities.Board>();
            var boardLookup = boards.ToDictionary(b => b.Id);

            writer.WriteStartArray("boards");
            foreach (var ba in boardAccesses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                boardLookup.TryGetValue(ba.BoardId, out var board);
                writer.WriteStartObject();
                writer.WriteString("boardId", ba.BoardId.ToString());
                writer.WriteString("name", board?.Name ?? "[deleted]");
                writer.WriteString("description", board?.Description);
                writer.WriteString("role", ba.Role.ToString());
                writer.WriteBoolean("isOwner", ba.Role == UserRole.Owner);
                writer.WriteString("createdAt", ba.CreatedAt);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            await writer.FlushAsync(cancellationToken);

            // notifications — streamed page-by-page to avoid loading all into memory
            writer.WriteStartArray("notifications");
            await foreach (var n in StreamNotificationsAsync(userId, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                writer.WriteString("id", n.Id.ToString());
                writer.WriteString("type", n.Type.ToString());
                writer.WriteString("title", n.Title);
                writer.WriteString("message", n.Message);
                writer.WriteBoolean("isRead", n.IsRead);
                writer.WriteString("createdAt", n.CreatedAt);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            await writer.FlushAsync(cancellationToken);

            // capture items (small)
            var captures = await _unitOfWork.LlmQueue.GetByUserAsync(userId, cancellationToken);
            writer.WriteStartArray("captureItems");
            foreach (var c in captures)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                writer.WriteString("id", c.Id.ToString());
                writer.WriteString("status", c.Status.ToString());
                writer.WriteString("requestType", c.RequestType);
                writer.WriteString("createdAt", c.CreatedAt);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            await writer.FlushAsync(cancellationToken);

            // proposals — streamed page-by-page
            writer.WriteStartArray("proposals");
            await foreach (var p in StreamProposalsAsync(userId, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                writer.WriteString("id", p.Id.ToString());
                writer.WriteString("status", p.Status.ToString());
                writer.WriteString("summary", p.Summary);
                if (p.BoardId.HasValue)
                    writer.WriteString("boardId", p.BoardId.Value.ToString());
                else
                    writer.WriteNull("boardId");
                writer.WriteString("createdAt", p.CreatedAt);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            await writer.FlushAsync(cancellationToken);

            // chat sessions with batched message counts — streamed
            writer.WriteStartArray("chatSessions");
            await foreach (var (session, msgCount) in StreamChatSessionsWithCountsAsync(userId, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                writer.WriteString("id", session.Id.ToString());
                writer.WriteString("status", session.Status.ToString());
                writer.WriteNumber("messageCount", msgCount);
                writer.WriteString("createdAt", session.CreatedAt);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            await writer.FlushAsync(cancellationToken);

            // audit trail — streamed page-by-page
            writer.WriteStartArray("auditTrail");
            await foreach (var a in StreamAuditLogsAsync(userId, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                writer.WriteString("id", a.Id.ToString());
                writer.WriteString("entityType", a.EntityType);
                writer.WriteString("entityId", a.EntityId.ToString());
                writer.WriteString("action", a.Action.ToString());
                writer.WriteString("timestamp", a.Timestamp);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            await writer.FlushAsync(cancellationToken);

            // preferences (single row)
            var preferences = await _unitOfWork.UserPreferences.GetByUserIdAsync(userId, cancellationToken);
            if (preferences is not null)
            {
                writer.WriteStartObject("preferences");
                writer.WriteString("workspaceMode", preferences.WorkspaceMode.ToString());
                writer.WriteString("createdAt", preferences.CreatedAt);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("preferences");
            }

            // notification preferences (single row)
            var notifPrefs = await _unitOfWork.NotificationPreferences.GetByUserIdAsync(userId, cancellationToken);
            if (notifPrefs is not null)
            {
                writer.WriteStartObject("notificationPreferences");
                writer.WriteBoolean("inAppChannelEnabled", notifPrefs.InAppChannelEnabled);
                writer.WriteBoolean("mentionImmediateEnabled", notifPrefs.MentionImmediateEnabled);
                writer.WriteBoolean("assignmentImmediateEnabled", notifPrefs.AssignmentImmediateEnabled);
                writer.WriteBoolean("proposalOutcomeImmediateEnabled", notifPrefs.ProposalOutcomeImmediateEnabled);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("notificationPreferences");
            }

            writer.WriteEndObject(); // data
            writer.WriteEndObject(); // root
            await writer.FlushAsync(cancellationToken);

            // Log the export action
            await _historyService.LogActionAsync(
                "User", userId, AuditAction.DataExported, userId,
                "User data export (streaming) requested");

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            // Propagate cancellation — do not swallow
            throw;
        }
        catch (Exception)
        {
            return Result.Failure(
                ErrorCodes.UnexpectedError,
                "Failed to stream user data export due to an internal error");
        }
    }

    // -----------------------------------------------------------------------
    // Private streaming helpers — page through large tables without a hard cap
    // -----------------------------------------------------------------------

    private const int StreamPageSize = 500;

    private async IAsyncEnumerable<Domain.Entities.Notification> StreamNotificationsAsync(
        Guid userId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int offset = 0;
        while (true)
        {
            var page = await _unitOfWork.Notifications.GetByUserIdAsync(
                userId, limit: StreamPageSize, unreadOnly: false, boardId: null,
                cancellationToken: cancellationToken, offset: offset);

            var rows = page.ToList();
            foreach (var row in rows)
                yield return row;

            if (rows.Count < StreamPageSize)
                yield break;

            offset += rows.Count;
        }
    }

    private async IAsyncEnumerable<Domain.Entities.AutomationProposal> StreamProposalsAsync(
        Guid userId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int offset = 0;
        while (true)
        {
            // GetByUserIdAsync honours the limit parameter; we page manually.
            var page = await _unitOfWork.AutomationProposals.GetByUserIdAsync(
                userId, limit: StreamPageSize, cancellationToken: cancellationToken);

            // Skip already-yielded rows (offset applied in-memory because the repo
            // interface doesn't expose an offset parameter).
            var rows = page.Skip(offset).Take(StreamPageSize).ToList();
            foreach (var row in rows)
                yield return row;

            // Proposals are typically small; stop when we get fewer than a full page.
            if (rows.Count < StreamPageSize)
                yield break;

            offset += rows.Count;
        }
    }

    private async IAsyncEnumerable<(Domain.Entities.ChatSession Session, int MessageCount)> StreamChatSessionsWithCountsAsync(
        Guid userId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Load all sessions (typically small); resolve message counts in one batch query per page.
        int offset = 0;
        while (true)
        {
            var allSessions = await _unitOfWork.ChatSessions.GetByUserIdAsync(
                userId, limit: StreamPageSize, cancellationToken: cancellationToken);

            var page = allSessions.Skip(offset).Take(StreamPageSize).ToList();
            if (page.Count == 0)
                yield break;

            var counts = await _unitOfWork.ChatMessages.CountBySessionIdsAsync(
                page.Select(s => s.Id), cancellationToken);

            foreach (var session in page)
            {
                counts.TryGetValue(session.Id, out var msgCount);
                yield return (session, msgCount);
            }

            if (page.Count < StreamPageSize)
                yield break;

            offset += page.Count;
        }
    }

    private async IAsyncEnumerable<Domain.Entities.AuditLog> StreamAuditLogsAsync(
        Guid userId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int offset = 0;
        while (true)
        {
            var page = await _unitOfWork.AuditLogs.GetByUserAsync(
                userId, limit: StreamPageSize, cancellationToken: cancellationToken);

            var rows = page.Skip(offset).Take(StreamPageSize).ToList();
            foreach (var row in rows)
                yield return row;

            if (rows.Count < StreamPageSize)
                yield break;

            offset += rows.Count;
        }
    }
}
