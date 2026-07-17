using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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
    private const long MaxBufferedArtefactBytes = ArtefactStorageSettings.DefaultMaxBytesPerArtefact;
    private const int MaxBufferedArtefactRows = 10_000;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IHistoryService _historyService;
    private readonly ILogger<DataExportService>? _logger;
    private readonly ISourceArtefactRepository _artefacts;
    private readonly IArtefactExtractionRepository _extractions;

    public DataExportService(
        IUnitOfWork unitOfWork,
        IHistoryService historyService,
        ISourceArtefactRepository artefacts,
        IArtefactExtractionRepository extractions,
        ILogger<DataExportService>? logger = null)
    {
        _unitOfWork = unitOfWork;
        _historyService = historyService;
        _logger = logger;
        _artefacts = artefacts;
        _extractions = extractions;
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
            var artefactBytes = await _artefacts.GetTotalByteSizeByUserAsync(userId, cancellationToken);
            var extractionBytes = await _extractions.GetEstimatedSerializedBytesByUserAsync(
                userId,
                cancellationToken);
            if (artefactBytes > MaxBufferedArtefactBytes ||
                extractionBytes > MaxBufferedArtefactBytes - artefactBytes)
            {
                return Result.Failure<UserDataExportDto>(
                    ErrorCodes.PayloadTooLarge,
                    "This export contains too much artefact or extraction content to buffer; use the streaming export endpoint");
            }

            var artefactMetadata = await GetBufferedArtefactMetadataAsync(userId, cancellationToken);
            if (artefactMetadata.Count > MaxBufferedArtefactRows)
            {
                return Result.Failure<UserDataExportDto>(
                    ErrorCodes.PayloadTooLarge,
                    "This export contains too many artefacts to buffer; use the streaming export endpoint");
            }

            // Gather all user-scoped data in parallel where safe
            var boardAccessesTask = _unitOfWork.BoardAccesses.GetByUserIdAsync(userId, cancellationToken);
            var notificationsTask = _unitOfWork.Notifications.GetByUserIdAsync(userId, limit: 10000, cancellationToken: cancellationToken);
            var capturesTask = _unitOfWork.LlmQueue.GetByUserAsync(userId, cancellationToken);
            // includeDeferred:true — a complete export must include the user's currently-snoozed
            // proposals, which review-queue reads hide.
            var proposalsTask = _unitOfWork.AutomationProposals.GetByUserIdAsync(userId, limit: 10000, includeDeferred: true, cancellationToken: cancellationToken);
            var chatSessionsTask = _unitOfWork.ChatSessions.GetByUserIdAsync(userId, limit: 10000, cancellationToken: cancellationToken);
            var auditLogsTask = _unitOfWork.AuditLogs.GetByUserAsync(userId, limit: 10000, cancellationToken: cancellationToken);
            var preferencesTask = _unitOfWork.UserPreferences.GetByUserIdAsync(userId, cancellationToken);
            var notificationPrefsTask = _unitOfWork.NotificationPreferences.GetByUserIdAsync(userId, cancellationToken);
            // Content-free per-user quality-feedback signals (#1245 review): user-scoped data that
            // the export must include for portability. Use the uncapped export read -- the cohort
            // helper's 1000-row cap would silently truncate a heavy reporter's export.
            var feedbackTask = _unitOfWork.ProposalFeedbacks.GetAllByUserIdForExportAsync(userId, cancellationToken);
            await Task.WhenAll(
                boardAccessesTask, notificationsTask, capturesTask,
                proposalsTask, chatSessionsTask, auditLogsTask,
                preferencesTask, notificationPrefsTask, feedbackTask);

            var boardAccesses = await boardAccessesTask;
            var notifications = await notificationsTask;
            var captures = await capturesTask;
            var proposals = await proposalsTask;
            var chatSessions = await chatSessionsTask;
            var auditLogs = await auditLogsTask;
            var preferences = await preferencesTask;
            var notificationPrefs = await notificationPrefsTask;
            var proposalFeedback = await feedbackTask;
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
                p.CreatedAt,
                p.DeferredUntil)).ToList();

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

            var exportFeedback = proposalFeedback.Select(f => new UserDataExportProposalFeedbackDto(
                f.ProposalId,
                f.Reason.ToString(),
                f.ReportedAt)).ToList();

            var exportArtefacts = new List<UserDataExportArtefactDto>(artefactMetadata.Count);
            // #1355: batch the blob loads in bounded chunks instead of one round-trip per artefact.
            // The chunk size mirrors StreamPageSize so the IN-clause stays within SQLite's parameter
            // limit and peak memory holds at most one chunk of raw blob bytes at a time (the buffered
            // total is already capped by MaxBufferedArtefactBytes). Metadata is iterated in its
            // original (Id) order, so the exported artefact array is byte-for-byte identical to the
            // former per-item path.
            for (var offset = 0; offset < artefactMetadata.Count; offset += StreamPageSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(StreamPageSize, artefactMetadata.Count - offset);
                var chunk = new List<Domain.Entities.SourceArtefact>(count);
                for (var i = offset; i < offset + count; i++)
                    chunk.Add(artefactMetadata[i]);

                var blobs = await _artefacts.GetContentsForUserAsync(
                    chunk.Select(a => a.Id).ToList(),
                    userId,
                    cancellationToken);

                foreach (var artefact in chunk)
                {
                    if (!blobs.TryGetValue(artefact.Id, out var bytes) || bytes is null)
                        throw new InvalidOperationException($"Artefact {artefact.Id} is missing its blob.");

                    var extractionHistory = await GetAllExtractionHistoryAsync(
                        artefact.Id,
                        userId,
                        cancellationToken);
                    exportArtefacts.Add(MapArtefactForExport(artefact, bytes, extractionHistory));
                }
            }

            var content = new UserDataExportContentDto(
                exportBoards,
                exportNotifications,
                exportCaptures,
                exportProposals,
                exportChatSessions,
                exportAuditEntries,
                exportPreferences,
                exportNotificationPrefs,
                exportFeedback,
                exportArtefacts);

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
            if (ex is not OperationCanceledException)
            {
                _logger?.LogError(ex, "Failed to export user data for user {UserId}", userId);
            }

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
                // Active snooze deadline (#1245 Codex review) — null unless the proposal is deferred.
                if (p.DeferredUntil.HasValue)
                    writer.WriteString("deferredUntil", p.DeferredUntil.Value);
                else
                    writer.WriteNull("deferredUntil");
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

            // proposal feedback — content-free signals, COMPLETE per-user set (#1245 review):
            // the uncapped export read so a heavy reporter's portability export isn't truncated.
            writer.WriteStartArray("proposalFeedback");
            foreach (var f in await _unitOfWork.ProposalFeedbacks.GetAllByUserIdForExportAsync(userId, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                writer.WriteString("proposalId", f.ProposalId.ToString());
                writer.WriteString("reason", f.Reason.ToString());
                writer.WriteString("reportedAt", f.ReportedAt);
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

            // Artefact content is the only export value that can exceed
            // Utf8JsonWriter's single-token Base64 limit. Flush and end this
            // writer segment while the data/root objects remain open; the
            // bounded raw tail below writes the final property and delimiters.
            // No writer calls may follow the raw tail because it owns those closes.
            await writer.FlushAsync(cancellationToken);
            await WriteArtefactsTailAsync(userId, destination, cancellationToken);

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

    private static readonly byte[] ArtefactsPropertyPrefix = ",\"artefacts\":["u8.ToArray();
    private static readonly byte[] ArtefactSeparator = ","u8.ToArray();
    private static readonly byte[] ArtefactContentPrefix = ",\"contentBase64\":\""u8.ToArray();
    private static readonly byte[] ArtefactExtractionsPrefix = "\",\"extractions\":["u8.ToArray();
    private static readonly byte[] ArtefactObjectSuffix = "]}"u8.ToArray();
    private static readonly byte[] ExportSuffix = "]}}"u8.ToArray();

    private const int StreamPageSize = 500;

    private async Task WriteArtefactsTailAsync(
        Guid userId,
        Stream destination,
        CancellationToken cancellationToken)
    {
        await destination.WriteAsync(ArtefactsPropertyPrefix, cancellationToken);

        var first = true;
        var offset = 0;
        while (true)
        {
            var page = await _artefacts.GetByUserAsync(
                userId,
                StreamPageSize,
                offset,
                cancellationToken);
            if (page.Count == 0)
                break;

            foreach (var artefact in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!first)
                    await destination.WriteAsync(ArtefactSeparator, cancellationToken);
                first = false;

                var metadataBuffer = new ArrayBufferWriter<byte>(512);
                using (var metadataWriter = new Utf8JsonWriter(metadataBuffer))
                {
                    metadataWriter.WriteStartObject();
                    metadataWriter.WriteString("id", artefact.Id);
                    if (artefact.BoardId.HasValue)
                        metadataWriter.WriteString("boardId", artefact.BoardId.Value);
                    else
                        metadataWriter.WriteNull("boardId");
                    metadataWriter.WriteString("kind", artefact.Kind.ToString());
                    metadataWriter.WriteString("mimeType", artefact.MimeType);
                    metadataWriter.WriteString("fileName", artefact.FileName);
                    metadataWriter.WriteNumber("byteSize", artefact.ByteSize);
                    metadataWriter.WriteString("sha256", artefact.Sha256);
                    metadataWriter.WriteString("captureSource", artefact.CaptureSource.ToString());
                    metadataWriter.WriteString("originReference", artefact.OriginReference);
                    if (artefact.CreatedFromCaptureId.HasValue)
                        metadataWriter.WriteString("createdFromCaptureId", artefact.CreatedFromCaptureId.Value);
                    else
                        metadataWriter.WriteNull("createdFromCaptureId");
                    metadataWriter.WriteString("createdAt", artefact.CreatedAt);
                    metadataWriter.WriteEndObject();
                    metadataWriter.Flush();
                }

                if (metadataBuffer.WrittenCount == 0 || metadataBuffer.WrittenSpan[^1] != (byte)'}')
                    throw new InvalidOperationException("Artefact export metadata was not a JSON object.");

                await destination.WriteAsync(metadataBuffer.WrittenMemory[..^1], cancellationToken);
                await destination.WriteAsync(ArtefactContentPrefix, cancellationToken);

                using var base64Transform = new ToBase64Transform();
                await using (var base64Stream = new CryptoStream(
                    destination,
                    base64Transform,
                    CryptoStreamMode.Write,
                    leaveOpen: true))
                {
                    var copied = await _artefacts.CopyContentForUserAsync(
                        artefact.Id,
                        userId,
                        base64Stream,
                        cancellationToken);
                    if (!copied)
                        throw new InvalidOperationException($"Artefact {artefact.Id} is missing its blob.");

                    await base64Stream.FlushFinalBlockAsync(cancellationToken);
                }

                await destination.WriteAsync(ArtefactExtractionsPrefix, cancellationToken);
                await WriteExtractionHistoryAsync(
                    artefact.Id,
                    userId,
                    destination,
                    cancellationToken);
                await destination.WriteAsync(ArtefactObjectSuffix, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }

            offset += page.Count;
            if (page.Count < StreamPageSize)
                break;
        }

        await destination.WriteAsync(ExportSuffix, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<Domain.Entities.SourceArtefact>> GetBufferedArtefactMetadataAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var all = new List<Domain.Entities.SourceArtefact>(MaxBufferedArtefactRows + 1);
        while (all.Count <= MaxBufferedArtefactRows)
        {
            var remaining = MaxBufferedArtefactRows + 1 - all.Count;
            var page = await _artefacts.GetByUserAsync(
                userId,
                Math.Min(StreamPageSize, remaining),
                all.Count,
                cancellationToken);
            all.AddRange(page);
            if (page.Count < StreamPageSize)
                return all;
        }

        return all;
    }

    private static UserDataExportArtefactDto MapArtefactForExport(
        Domain.Entities.SourceArtefact artefact,
        byte[] content,
        IReadOnlyList<UserDataExportArtefactExtractionDto> extractions)
        => new(
            artefact.Id,
            artefact.BoardId,
            artefact.Kind.ToString(),
            artefact.MimeType,
            artefact.FileName,
            artefact.ByteSize,
            artefact.Sha256,
            artefact.CaptureSource.ToString(),
            artefact.OriginReference,
            artefact.CreatedFromCaptureId,
            artefact.CreatedAt,
            Convert.ToBase64String(content),
            extractions);

    private async Task<IReadOnlyList<UserDataExportArtefactExtractionDto>> GetAllExtractionHistoryAsync(
        Guid artefactId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var all = new List<UserDataExportArtefactExtractionDto>();
        while (true)
        {
            var page = await _extractions.GetByArtefactForUserAsync(
                artefactId,
                userId,
                limit: 50,
                offset: all.Count,
                cancellationToken: cancellationToken);
            all.AddRange(page.Select(MapExtractionForExport));
            if (page.Count < 50)
                return all;
        }
    }

    private async Task WriteExtractionHistoryAsync(
        Guid artefactId,
        Guid userId,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var first = true;
        var offset = 0;
        while (true)
        {
            var page = await _extractions.GetByArtefactForUserAsync(
                artefactId,
                userId,
                limit: 50,
                offset: offset,
                cancellationToken: cancellationToken);
            foreach (var extraction in page)
            {
                if (!first)
                    await destination.WriteAsync(ArtefactSeparator, cancellationToken);
                first = false;

                var buffer = new ArrayBufferWriter<byte>(
                    Math.Min(extraction.TextLength + 512, 64 * 1024));
                using (var writer = new Utf8JsonWriter(buffer))
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", extraction.Id);
                    writer.WriteString("extractorName", extraction.ExtractorName);
                    writer.WriteString("extractorVersion", extraction.ExtractorVersion);
                    writer.WriteStartArray("warnings");
                    foreach (var warning in extraction.Warnings)
                        writer.WriteStringValue(warning);
                    writer.WriteEndArray();
                    writer.WriteString("extractedText", extraction.ExtractedText);
                    writer.WriteNumber("textLength", extraction.TextLength);
                    writer.WriteString("createdAt", extraction.CreatedAt);
                    writer.WriteEndObject();
                    writer.Flush();
                }

                await destination.WriteAsync(buffer.WrittenMemory, cancellationToken);
            }

            offset += page.Count;
            if (page.Count < 50)
                return;
        }
    }

    private static UserDataExportArtefactExtractionDto MapExtractionForExport(
        Domain.Entities.ArtefactExtraction extraction)
        => new(
            extraction.Id,
            extraction.ExtractorName,
            extraction.ExtractorVersion,
            extraction.Warnings,
            extraction.ExtractedText,
            extraction.TextLength,
            extraction.CreatedAt);

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
        // IAutomationProposalRepository.GetByUserIdAsync does not expose an offset parameter,
        // so we cannot page at the DB level. Load all rows in a single query using int.MaxValue
        // as the limit — EF Core translates this to LIMIT 2147483647 which effectively removes
        // the cap. The repo's NormalizeLimit guard only triggers for limit <= 0 and leaves
        // positive values unchanged.
        var all = await _unitOfWork.AutomationProposals.GetByUserIdAsync(
            userId, limit: int.MaxValue, includeDeferred: true, cancellationToken: cancellationToken);

        foreach (var p in all)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return p;
        }
    }

    private async IAsyncEnumerable<(Domain.Entities.ChatSession Session, int MessageCount)> StreamChatSessionsWithCountsAsync(
        Guid userId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // IChatSessionRepository.GetByUserIdAsync does not expose an offset parameter.
        // Load all sessions in one shot and resolve message counts in batches of StreamPageSize
        // to bound the IN-clause size passed to CountBySessionIdsAsync.
        var allSessions = (await _unitOfWork.ChatSessions.GetByUserIdAsync(
            userId, limit: int.MaxValue, cancellationToken: cancellationToken)).ToList();

        for (int i = 0; i < allSessions.Count; i += StreamPageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = allSessions.Skip(i).Take(StreamPageSize).ToList();
            var counts = await _unitOfWork.ChatMessages.CountBySessionIdsAsync(
                batch.Select(s => s.Id), cancellationToken);

            foreach (var session in batch)
            {
                counts.TryGetValue(session.Id, out var msgCount);
                yield return (session, msgCount);
            }
        }
    }

    private async IAsyncEnumerable<Domain.Entities.AuditLog> StreamAuditLogsAsync(
        Guid userId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // IAuditLogRepository.GetByUserAsync does not expose an offset parameter.
        // Load all rows in one shot — this is the table most likely to be large so
        // we accept the tradeoff of a single large DB read vs. correctness of the
        // pagination. A future improvement would add offset support to the repo.
        var all = await _unitOfWork.AuditLogs.GetByUserAsync(
            userId, limit: int.MaxValue, cancellationToken: cancellationToken);

        foreach (var a in all)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return a;
        }
    }
}
