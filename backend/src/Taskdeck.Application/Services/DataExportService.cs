using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
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

    private async IAsyncEnumerable<CardDto> StreamCardsAsync(Guid userId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const int pageSize = 500;
        for (var offset = 0; ; offset += pageSize)
        {
            var page = await _unitOfWork.Cards.GetExportPageByUserIdAsync(userId, offset, pageSize, cancellationToken);
            foreach (var card in page) yield return CardService.MapToDto(card);
            if (page.Count < pageSize) yield break;
        }
    }
    private const long MaxBufferedArtefactBytes = ArtefactStorageSettings.DefaultMaxBytesPerArtefact;
    private const int MaxBufferedArtefactRows = 10_000;
    private const long MaxBufferedTranscriptSerializedCharacters = 1_024_000;
    private const int MaxBufferedTranscriptRows = 10_000;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IHistoryService _historyService;
    private readonly ILogger<DataExportService>? _logger;
    private readonly ISourceArtefactRepository _artefacts;
    private readonly IArtefactExtractionRepository _extractions;
    private readonly ITranscriptRepository _transcripts;
    private readonly IWorkspaceInsightRepository _workspaceInsights;
    private static readonly JsonSerializerOptions PortabilityJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The durable capture aggregate (ADR-0065 / CF-01 #2255). Optional so hosts and tests that
    /// never wired it keep exporting exactly the previous package; when present, every capture in
    /// the package also carries its <c>Captures</c> row and its immutable <c>SourceAsset</c>s.
    /// </summary>
    private readonly ICaptureStore? _captureStore;
    private readonly ISourcePortabilityStore? _sourceStorage;
    private readonly IBoardDependencyRepository? _dependencies;

    /// <summary>Bounds one durable-capture lookup; kept under the 900-id batch cap the repositories share.</summary>
    private const int DurableCaptureChunkSize = 500;

    public DataExportService(
        IUnitOfWork unitOfWork,
        IHistoryService historyService,
        ISourceArtefactRepository artefacts,
        IArtefactExtractionRepository extractions,
        ITranscriptRepository transcripts,
        IWorkspaceInsightRepository workspaceInsights,
        ILogger<DataExportService>? logger = null,
        ICaptureStore? captureStore = null,
        ISourcePortabilityStore? sourceStorage = null,
        IBoardDependencyRepository? dependencies = null)
    {
        _unitOfWork = unitOfWork;
        _historyService = historyService;
        _logger = logger;
        _artefacts = artefacts;
        _extractions = extractions;
        _transcripts = transcripts;
        _workspaceInsights = workspaceInsights;
        _captureStore = captureStore;
        _sourceStorage = sourceStorage;
        _dependencies = dependencies;
    }

    /// <summary>
    /// Loads the durable captures behind the exported capture rows, owner-scoped and chunked. Ids
    /// without a durable row are simply absent: an install that has never enabled the dual-write
    /// exports exactly the package it exported before.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, Domain.Entities.Capture>> LoadDurableCapturesAsync(
        Guid userId,
        IReadOnlyList<Guid> captureIds,
        CancellationToken cancellationToken)
    {
        if (_captureStore is null || captureIds.Count == 0)
        {
            return new Dictionary<Guid, Domain.Entities.Capture>();
        }

        var result = new Dictionary<Guid, Domain.Entities.Capture>(captureIds.Count);
        for (var offset = 0; offset < captureIds.Count; offset += DurableCaptureChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = captureIds.Skip(offset).Take(DurableCaptureChunkSize).ToArray();
            foreach (var capture in await _captureStore.GetByIdsForUserAsync(chunk, userId, cancellationToken))
            {
                result[capture.Id] = capture;
            }
        }

        return result;
    }

    /// <summary>
    /// The queue-row material, for a capture with no durable row. Without this an unbackfillable
    /// capture would export as an id, a status and nothing the user could actually read back.
    /// </summary>
    private static UserDataExportCaptureSourceDto MapLegacyCaptureSource(CapturePayloadV1 payload) =>
        new(payload.Source.ToString(), payload.Text, payload.TitleHint, payload.ExternalRef);

    internal static UserDataExportDurableCaptureDto? MapDurableCapture(Domain.Entities.Capture? capture)
    {
        if (capture is null)
        {
            return null;
        }

        return new UserDataExportDurableCaptureDto(
            capture.Disposition.ToString(),
            capture.ProcessingSummary.ToString(),
            capture.ActionState.ToString(),
            capture.Timeline.ToString(),
            capture.ProducerKind.ToString(),
            capture.RequestedIntent.ToString(),
            capture.EffectiveIntent?.ToString(),
            capture.PrimaryModality.ToString(),
            capture.OriginAdapter.ToString(),
            capture.LegacySourceSnapshot.ToString(),
            capture.CapturedAtServer,
            capture.CapturedAtClient,
            capture.UserTitle,
            capture.UserNote,
            capture.SourceAssets
                .Select(asset => new UserDataExportSourceAssetDto(
                    asset.Id,
                    asset.Ordinal,
                    asset.Modality.ToString(),
                    asset.MediaType,
                    asset.ContentHash,
                    asset.ByteSize,
                    asset.StorageKind.ToString(),
                    asset.ExternalReference,
                    asset.OriginalName,
                    asset.SupersedesAssetId,
                    asset.SupersededByAssetId,
                    asset.TextPayload?.Text,
                    asset.BlobReferenceId))
                .ToList());
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
            if (_sourceStorage is not null && await _sourceStorage.EstimateBufferedBytesAsync(userId, cancellationToken) > 25L * 1024 * 1024)
                return Result.Failure<UserDataExportDto>(ErrorCodes.PayloadTooLarge, "This export contains too much original source content to buffer; use the streaming export endpoint");
            var artefactBytes = await _artefacts.GetTotalByteSizeByUserAsync(userId, cancellationToken);
            var extractionBytes = await _extractions.GetEstimatedSerializedBytesByUserAsync(
                userId,
                cancellationToken);
            var transcriptSerializedCharacters = await _transcripts.GetEstimatedSerializedLengthByUserAsync(
                userId,
                cancellationToken);
            if (artefactBytes > MaxBufferedArtefactBytes ||
                extractionBytes > MaxBufferedArtefactBytes - artefactBytes)
            {
                return Result.Failure<UserDataExportDto>(
                    ErrorCodes.PayloadTooLarge,
                    "This export contains too much artefact or extraction content to buffer; use the streaming export endpoint");
            }
            if (transcriptSerializedCharacters > MaxBufferedTranscriptSerializedCharacters)
            {
                return Result.Failure<UserDataExportDto>(
                    ErrorCodes.PayloadTooLarge,
                    "This export contains too much transcript content to buffer; use the streaming export endpoint");
            }

            var artefactMetadata = await GetBufferedArtefactMetadataAsync(userId, cancellationToken);
            if (artefactMetadata.Count > MaxBufferedArtefactRows)
            {
                return Result.Failure<UserDataExportDto>(
                    ErrorCodes.PayloadTooLarge,
                    "This export contains too many artefacts to buffer; use the streaming export endpoint");
            }
            var transcriptMetadata = await GetBufferedTranscriptMetadataAsync(userId, cancellationToken);
            if (transcriptMetadata.Count > MaxBufferedTranscriptRows)
            {
                return Result.Failure<UserDataExportDto>(
                    ErrorCodes.PayloadTooLarge,
                    "This export contains too many transcripts to buffer; use the streaming export endpoint");
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

            var resolvedCaptures = await ResolveCaptureExportsAsync(captures, cancellationToken);
            var durableCaptures = await LoadDurableCapturesAsync(
                userId,
                resolvedCaptures.Select(c => c.Request.Id).ToList(),
                cancellationToken);
            var exportCaptures = resolvedCaptures.Select(c =>
            {
                return new UserDataExportCaptureDto(
                    c.Request.Id,
                    c.Request.Status.ToString(),
                    c.Request.RequestType,
                    c.Request.CreatedAt,
                    c.BoardId,
                    c.Payload.Provenance,
                    c.Payload.Disposition is null
                        ? null
                        : new UserDataExportCaptureDispositionDto(
                            c.Payload.Disposition.Kind.ToString(),
                            c.Payload.Disposition.At,
                            c.Payload.Disposition.ByUserId,
                            c.Payload.Disposition.BoardId),
                    MapDurableCapture(durableCaptures.GetValueOrDefault(c.Request.Id)),
                    durableCaptures.ContainsKey(c.Request.Id) ? null : MapLegacyCaptureSource(c.Payload));
            }).ToList();

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
                    preferences.CreatedAt,
                    preferences.ReadPersonalPlan(),
                    preferences.PersonalPlanRevision,
                    preferences.ReadAttention(), preferences.AttentionRevision)
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

            var exportTranscripts = transcriptMetadata.Select(MapTranscriptForExport).ToList();

            var exportArtefacts = new List<UserDataExportArtefactDto>(artefactMetadata.Count);
            // #1355: batch the blob loads in bounded chunks instead of one round-trip per artefact.
            // The chunk size mirrors StreamPageSize so each IN-clause stays well within SQLite's
            // parameter budget. Memory: the raw byte[] dictionary holds at most one chunk at a time
            // (released between chunks), while the mapped DTOs' base64 strings DO accumulate across
            // chunks — both halves stay bounded because the pre-load MaxBufferedArtefactBytes guard
            // caps the total artefact bytes this path may buffer. Metadata keeps its original (Id)
            // order, so the exported artefact array is byte-for-byte identical to the former
            // per-item path.
            foreach (var chunk in artefactMetadata.Chunk(StreamPageSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunkIds = chunk.Select(a => a.Id).ToList();
                var blobs = await _artefacts.GetContentsForUserAsync(
                    chunkIds,
                    userId,
                    cancellationToken);

                // #1387: batch the per-artefact extraction-history loads in the same 500-id chunks
                // as the blob batch above, replacing the former one-paged-query-per-artefact N+1
                // (GetAllExtractionHistoryAsync). The batch groups results per artefact in
                // CreatedAt ASC, Id ASC order — identical to the former sequential paging — so the
                // exported extractions stay byte-for-byte identical. Peak memory stays bounded: the
                // pre-load MaxBufferedArtefactBytes guard already caps total extraction bytes across
                // the whole export, so a single chunk's grouped history can never exceed that cap.
                var extractionsByArtefact = await _extractions.GetByArtefactsForUserAsync(
                    chunkIds,
                    userId,
                    cancellationToken);

                foreach (var artefact in chunk)
                {
                    if (!blobs.TryGetValue(artefact.Id, out var bytes) || bytes is null)
                        throw new InvalidOperationException($"Artefact {artefact.Id} is missing its blob.");

                    var extractionHistory = extractionsByArtefact.TryGetValue(artefact.Id, out var extractions)
                        ? extractions.Select(MapExtractionForExport).ToList()
                        : (IReadOnlyList<UserDataExportArtefactExtractionDto>)Array.Empty<UserDataExportArtefactExtractionDto>();
                    exportArtefacts.Add(MapArtefactForExport(artefact, bytes, extractionHistory));
                }
            }

            var exportMemories = new List<UserDataExportWorkspaceMemoryDto>();
            await foreach (var memory in StreamWorkspaceMemoriesAsync(userId, cancellationToken))
            {
                if (exportMemories.Count >= 10_000)
                    return Result.Failure<UserDataExportDto>(ErrorCodes.PayloadTooLarge, "Too many private memories to buffer; use the streaming export endpoint.");
                exportMemories.Add(MapWorkspaceMemory(memory));
            }
            var exportInsights = new List<UserDataExportQuietInsightDto>();
            await foreach (var insight in StreamQuietInsightsAsync(userId, cancellationToken))
            {
                if (exportInsights.Count >= 10_000)
                    return Result.Failure<UserDataExportDto>(ErrorCodes.PayloadTooLarge, "Too many quiet insights to buffer; use the streaming export endpoint.");
                exportInsights.Add(MapQuietInsight(insight));
            }

            var nativeCaptures = new List<UserDataExportNativeCaptureDto>();
            long nativeBytes = 0;
            await foreach (var capture in StreamNativeCapturesAsync(userId, cancellationToken))
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(capture, PortabilityJsonOptions).LongLength;
                nativeBytes += bytes;
                if (nativeCaptures.Count >= 10_000 || nativeBytes > 25 * 1024 * 1024)
                    return Result.Failure<UserDataExportDto>(ErrorCodes.PayloadTooLarge, "Too many native originals to buffer; use the streaming export endpoint.");
                nativeCaptures.Add(capture);
            }
            var exportCards = new List<CardDto>();
            await foreach (var card in StreamCardsAsync(userId, cancellationToken))
            {
                if (exportCards.Count >= 10_000)
                    return Result.Failure<UserDataExportDto>(ErrorCodes.PayloadTooLarge, "Too many cards to buffer; use the streaming export endpoint.");
                exportCards.Add(card);
            }
            var exportRelations = await LoadRelationsForExportAsync(
                exportCards, cancellationToken);
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
                exportArtefacts,
                exportTranscripts,
                exportMemories,
                exportInsights,
                nativeCaptures,
                await BufferSourceStorageAsync(userId, cancellationToken), exportCards, exportRelations);

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
        catch (DomainException ex) when (ex.ErrorCode == ErrorCodes.PayloadTooLarge)
        {
            return Result.Failure<UserDataExportDto>(ex.ErrorCode, ex.Message);
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

            // Keep only relation candidates and their endpoints. Card exports can be arbitrarily
            // large, while each board relation graph is capped at 500 rows.
            var streamedRelationScopes = new Dictionary<Guid, RelationExportScope?>();
            writer.WriteStartArray("cards");
            await foreach (var card in StreamCardsAsync(userId, cancellationToken))
            {
                if (!streamedRelationScopes.TryGetValue(card.BoardId, out var relationScope))
                {
                    relationScope = await LoadRelationScopeAsync(card.BoardId, cancellationToken);
                    streamedRelationScopes.Add(card.BoardId, relationScope);
                }
                relationScope?.Observe(card.Id);
                JsonSerializer.SerializeToElement(card, PortabilityJsonOptions).WriteTo(writer);
                await writer.FlushAsync(cancellationToken);
            }
            writer.WriteEndArray();
            await writer.FlushAsync(cancellationToken);

            writer.WriteStartArray("relations");
            foreach (var (boardId, relationScope) in streamedRelationScopes)
            {
                if (relationScope is null)
                    continue;
                foreach (var edge in relationScope.ReadExportableRelations())
                {
                    JsonSerializer.SerializeToElement(new UserDataExportCardRelationDto(
                        boardId, edge.SourceCardId, edge.TargetCardId, edge.RelationType), PortabilityJsonOptions).WriteTo(writer);
                    await writer.FlushAsync(cancellationToken);
                }
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
            var resolvedCaptures = await ResolveCaptureExportsAsync(captures, cancellationToken);
            writer.WriteStartArray("captureItems");
            // Durable graphs are loaded a page at a time and written as they arrive. Buffering them
            // all first would defeat the point of the streaming export: every capture, every
            // superseded revision of every capture, resident at once.
            for (var offset = 0; offset < resolvedCaptures.Count; offset += DurableCaptureChunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = resolvedCaptures.Skip(offset).Take(DurableCaptureChunkSize).ToList();
                var pageDurableCaptures = await LoadDurableCapturesAsync(
                    userId,
                    page.Select(c => c.Request.Id).ToList(),
                    cancellationToken);

                foreach (var c in page)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    writer.WriteStartObject();
                    writer.WriteString("id", c.Request.Id.ToString());
                    writer.WriteString("status", c.Request.Status.ToString());
                    writer.WriteString("requestType", c.Request.RequestType);
                    writer.WriteString("createdAt", c.Request.CreatedAt);
                    WriteNullableGuid(writer, "boardId", c.BoardId);
                    WriteCaptureProvenance(writer, c.Payload.Provenance);
                    WriteCaptureDisposition(writer, c.Payload.Disposition);
                    var durable = pageDurableCaptures.GetValueOrDefault(c.Request.Id);
                    WriteDurableCapture(writer, durable);
                    WriteLegacyCaptureSource(writer, durable is null ? c.Payload : null);
                    writer.WriteEndObject();
                }

                await writer.FlushAsync(cancellationToken);
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
                writer.WritePropertyName("personalPlan");
                // Serialize the bounded plan away from the response stream: Serialize(writer)
                // flushes synchronously, which ASP.NET rejects on this streaming endpoint.
                JsonSerializer.SerializeToElement(preferences.ReadPersonalPlan(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }).WriteTo(writer);
                writer.WriteNumber("personalPlanRevision", preferences.PersonalPlanRevision);
                writer.WritePropertyName("attention");
                JsonSerializer.SerializeToElement(preferences.ReadAttention(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }).WriteTo(writer);
                writer.WriteNumber("attentionRevision", preferences.AttentionRevision);
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

            writer.WriteStartArray("nativeCaptures");
            await foreach (var capture in StreamNativeCapturesAsync(userId, cancellationToken))
            {
                writer.WriteRawValue(JsonSerializer.SerializeToUtf8Bytes(capture, PortabilityJsonOptions));
                await writer.FlushAsync(cancellationToken);
            }
            writer.WriteEndArray();
            if (_sourceStorage is null) writer.WriteNull("sourceStorage");
            else
            {
                writer.WriteStartObject("sourceStorage");
                await using var sourceSnapshot = await _sourceStorage.OpenReadSnapshotAsync(cancellationToken);
                await WriteSourceRowsAsync(writer, "objects", _sourceStorage.ObjectsAsync(userId, cancellationToken), cancellationToken);
                await WriteSourceRowsAsync(writer, "references", _sourceStorage.ReferencesAsync(userId, cancellationToken), cancellationToken);
                await WriteSourceRowsAsync(writer, "chunks", _sourceStorage.ChunksAsync(userId, cancellationToken), cancellationToken);
                await WriteSourceRowsAsync(writer, "representations", _sourceStorage.RepresentationsAsync(userId, cancellationToken), cancellationToken);
                await WriteSourceRowsAsync(writer, "audioAnswers", _sourceStorage.AudioAnswersAsync(userId, cancellationToken), cancellationToken);
                await WriteSourceRowsAsync(writer, "audioTranscriptionAttempts", _sourceStorage.AudioTranscriptionAttemptsAsync(userId, cancellationToken), cancellationToken);
                await WriteSourceRowsAsync(writer, "audioTranscriptionBudgets", _sourceStorage.AudioTranscriptionBudgetsAsync(userId, cancellationToken), cancellationToken);
                writer.WriteEndObject();
            }
            writer.WriteStartArray("workspaceMemories");
            await foreach (var memory in StreamWorkspaceMemoriesAsync(userId, cancellationToken))
            {
                // Serialize a single row before writing; Serialize(writer, ...) flushes
                // synchronously and ASP.NET response streams prohibit synchronous writes.
                writer.WriteRawValue(JsonSerializer.SerializeToUtf8Bytes(MapWorkspaceMemory(memory), PortabilityJsonOptions));
                await writer.FlushAsync(cancellationToken);
            }
            writer.WriteEndArray();
            writer.WriteStartArray("quietInsights");
            await foreach (var insight in StreamQuietInsightsAsync(userId, cancellationToken))
            {
                writer.WriteRawValue(JsonSerializer.SerializeToUtf8Bytes(MapQuietInsight(insight), PortabilityJsonOptions));
                await writer.FlushAsync(cancellationToken);
            }
            writer.WriteEndArray();

            writer.WriteStartArray("transcripts");
            await foreach (var transcript in StreamTranscriptsAsync(userId, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                writer.WriteString("id", transcript.Id);
                if (transcript.BoardId.HasValue)
                    writer.WriteString("boardId", transcript.BoardId.Value);
                else
                    writer.WriteNull("boardId");
                writer.WriteString("captureSource", transcript.CaptureSource.ToString());
                writer.WriteString("text", transcript.Text);
                writer.WriteStartArray("segments");
                foreach (var segment in transcript.Segments)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("startLine", segment.StartLine);
                    writer.WriteNumber("endLine", segment.EndLine);
                    writer.WriteString("speaker", segment.Speaker);
                    if (segment.TimestampMilliseconds.HasValue)
                        writer.WriteNumber("timestampMilliseconds", segment.TimestampMilliseconds.Value);
                    else
                        writer.WriteNull("timestampMilliseconds");
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                if (transcript.CreatedFromCaptureId.HasValue)
                    writer.WriteString("createdFromCaptureId", transcript.CreatedFromCaptureId.Value);
                else
                    writer.WriteNull("createdFromCaptureId");
                if (transcript.SourceArtefactId.HasValue)
                    writer.WriteString("sourceArtefactId", transcript.SourceArtefactId.Value);
                else
                    writer.WriteNull("sourceArtefactId");
                writer.WriteString("createdAt", transcript.CreatedAt);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            await writer.FlushAsync(cancellationToken);

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

    private async Task<IReadOnlyList<ResolvedCaptureExport>> ResolveCaptureExportsAsync(
        IEnumerable<Domain.Entities.LlmRequest> captures,
        CancellationToken cancellationToken)
    {
        var parsed = captures
            .Select(request => new ResolvedCaptureExport(
                request,
                CaptureRequestContract.ParseStoredPayload(request.Payload),
                request.BoardId))
            .ToList();
        var proposalIds = parsed
            .Select(capture => capture.Payload.Provenance?.ProposalId)
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var proposals = proposalIds.Length == 0
            ? Array.Empty<Domain.Entities.AutomationProposal>()
            : await _unitOfWork.AutomationProposals.GetByIdsAsync(proposalIds, cancellationToken)
              ?? Array.Empty<Domain.Entities.AutomationProposal>();
        var proposalLookup = proposals.ToDictionary(proposal => proposal.Id);

        return parsed.Select(capture =>
        {
            var provenance = capture.Payload.Provenance;
            if (provenance?.ProposalId is not { } proposalId ||
                !proposalLookup.TryGetValue(proposalId, out var proposal) ||
                !CaptureEffectiveBoardPolicy.IsValidatedAppliedProposal(
                    capture.Request.Id,
                    capture.Request.UserId,
                    proposalId,
                    provenance.ConvertedAt,
                    proposal))
            {
                return capture;
            }

            var boardId = CaptureEffectiveBoardPolicy.ResolveEffectiveBoardId(
                capture.Request.Id,
                capture.Request.UserId,
                capture.Request.BoardId,
                provenance.BoardId,
                proposalId,
                provenance.ConvertedAt,
                proposal);
            var payload = CaptureRequestContract.WithProvenance(
                capture.Payload,
                capture.Request.Id,
                proposalId: proposal.Id,
                boardId: boardId,
                convertedAt: CaptureConversionTimestamp.ResolveConvertedAt(proposal.AppliedAt));
            return new ResolvedCaptureExport(capture.Request, payload, boardId);
        }).ToList();
    }

    private sealed record ResolvedCaptureExport(
        Domain.Entities.LlmRequest Request,
        CapturePayloadV1 Payload,
        Guid? BoardId);

    private static void WriteCaptureProvenance(Utf8JsonWriter writer, CaptureProvenanceV1? provenance)
    {
        writer.WritePropertyName("provenance");
        if (provenance is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("captureItemId", provenance.CaptureItemId);
        WriteNullableGuid(writer, "triageRunId", provenance.TriageRunId);
        WriteNullableGuid(writer, "proposalId", provenance.ProposalId);
        writer.WriteString("promptVersion", provenance.PromptVersion);
        writer.WriteString("provider", provenance.Provider);
        writer.WriteString("model", provenance.Model);
        WriteNullableGuid(writer, "requestedByUserId", provenance.RequestedByUserId);
        writer.WriteString("correlationId", provenance.CorrelationId);
        writer.WriteString("sourceSurface", provenance.SourceSurface);
        WriteNullableGuid(writer, "boardId", provenance.BoardId);
        WriteNullableGuid(writer, "sessionId", provenance.SessionId);
        if (provenance.ConvertedAt.HasValue)
            writer.WriteString("convertedAt", provenance.ConvertedAt.Value);
        else
            writer.WriteNull("convertedAt");
        writer.WriteEndObject();
    }

    private static void WriteCaptureDisposition(Utf8JsonWriter writer, CaptureDispositionV1? disposition)
    {
        writer.WritePropertyName("disposition");
        if (disposition is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("kind", disposition.Kind.ToString());
        writer.WriteString("at", disposition.At);
        writer.WriteString("byUserId", disposition.ByUserId);
        WriteNullableGuid(writer, "boardId", disposition.BoardId);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Streams the durable capture beside its queue row. Emitted as <c>null</c> when the capture has
    /// no durable row, so the streaming package and the buffered package stay the same shape.
    /// </summary>
    private static void WriteDurableCapture(Utf8JsonWriter writer, Domain.Entities.Capture? capture)
    {
        writer.WritePropertyName("durableCapture");
        if (capture is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("disposition", capture.Disposition.ToString());
        writer.WriteString("processingSummary", capture.ProcessingSummary.ToString());
        writer.WriteString("actionState", capture.ActionState.ToString());
        writer.WriteString("timeline", capture.Timeline.ToString());
        writer.WriteString("producerKind", capture.ProducerKind.ToString());
        writer.WriteString("requestedIntent", capture.RequestedIntent.ToString());
        writer.WriteString("effectiveIntent", capture.EffectiveIntent?.ToString());
        writer.WriteString("primaryModality", capture.PrimaryModality.ToString());
        writer.WriteString("originAdapter", capture.OriginAdapter.ToString());
        writer.WriteString("legacySourceSnapshot", capture.LegacySourceSnapshot.ToString());
        writer.WriteString("capturedAtServer", capture.CapturedAtServer);
        if (capture.CapturedAtClient.HasValue)
            writer.WriteString("capturedAtClient", capture.CapturedAtClient.Value);
        else
            writer.WriteNull("capturedAtClient");
        writer.WriteString("userTitle", capture.UserTitle);
        writer.WriteString("userNote", capture.UserNote);
        writer.WriteStartArray("sourceAssets");
        foreach (var asset in capture.SourceAssets)
        {
            writer.WriteStartObject();
            writer.WriteString("id", asset.Id);
            writer.WriteNumber("ordinal", asset.Ordinal);
            writer.WriteString("modality", asset.Modality.ToString());
            writer.WriteString("mediaType", asset.MediaType);
            writer.WriteString("contentHash", asset.ContentHash);
            writer.WriteNumber("byteSize", asset.ByteSize);
            writer.WriteString("storageKind", asset.StorageKind.ToString());
            writer.WriteString("externalReference", asset.ExternalReference);
            writer.WriteString("originalName", asset.OriginalName);
            WriteNullableGuid(writer, "supersedesAssetId", asset.SupersedesAssetId);
            WriteNullableGuid(writer, "supersededByAssetId", asset.SupersededByAssetId);
            writer.WriteString("text", asset.TextPayload?.Text);
            if (asset.BlobReferenceId.HasValue) writer.WriteString("blobReferenceId", asset.BlobReferenceId.Value);
            else writer.WriteNull("blobReferenceId");
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>Streams the queue-row material for a capture with no durable row; null otherwise.</summary>
    private static void WriteLegacyCaptureSource(Utf8JsonWriter writer, CapturePayloadV1? payload)
    {
        writer.WritePropertyName("legacySource");
        if (payload is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("source", payload.Source.ToString());
        writer.WriteString("text", payload.Text);
        writer.WriteString("titleHint", payload.TitleHint);
        writer.WriteString("externalRef", payload.ExternalRef);
        writer.WriteEndObject();
    }

    private static void WriteNullableGuid(Utf8JsonWriter writer, string propertyName, Guid? value)
    {
        if (value.HasValue)
            writer.WriteString(propertyName, value.Value);
        else
            writer.WriteNull(propertyName);
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

    // Coupled constraint (#1387): this is also the buffered export's batch-chunk size for
    // ISourceArtefactRepository.GetContentsForUserAsync and
    // IArtefactExtractionRepository.GetByArtefactsForUserAsync, whose implementations cap a batch
    // at MaxBatchIdCount (900) raw ids. StreamPageSize must stay <= 900 or the buffered export
    // throws ArgumentException at runtime.
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

    private async Task<IReadOnlyList<Domain.Entities.Transcript>> GetBufferedTranscriptMetadataAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var all = new List<Domain.Entities.Transcript>(MaxBufferedTranscriptRows + 1);
        while (all.Count <= MaxBufferedTranscriptRows)
        {
            var remaining = MaxBufferedTranscriptRows + 1 - all.Count;
            var page = await _transcripts.GetByUserAsync(
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

    private static UserDataExportTranscriptDto MapTranscriptForExport(
        Domain.Entities.Transcript transcript)
        => new(
            transcript.Id,
            transcript.BoardId,
            transcript.CaptureSource.ToString(),
            transcript.Text,
            transcript.Segments.Select(segment => new UserDataExportTranscriptSegmentDto(
                segment.StartLine,
                segment.EndLine,
                segment.Speaker,
                segment.TimestampMilliseconds)).ToList(),
            transcript.CreatedFromCaptureId,
            transcript.SourceArtefactId,
            transcript.CreatedAt);

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

    /// <summary>
    /// The account export owns no separate relation permission. Its card export is the scope:
    /// a card page contains every card, including archived cards, on boards the user owns or can
    /// read. A relation is emitted only for one of those complete board scopes, so a shared board
    /// never exposes links from an unrelated board or a private card collection.
    /// </summary>
    private async Task<IReadOnlyList<UserDataExportCardRelationDto>> LoadRelationsForExportAsync(
        IEnumerable<CardDto> exportedCards,
        CancellationToken cancellationToken)
        => await LoadRelationsForExportAsync(
            exportedCards.GroupBy(card => card.BoardId)
                .ToDictionary(group => group.Key, group => group.Select(card => card.Id).ToHashSet()),
            cancellationToken);

    private async Task<IReadOnlyList<UserDataExportCardRelationDto>> LoadRelationsForExportAsync(
        IReadOnlyDictionary<Guid, HashSet<Guid>> exportedCardIdsByBoard,
        CancellationToken cancellationToken)
    {
        if (_dependencies is null)
            return [];

        var relations = new List<UserDataExportCardRelationDto>();
        foreach (var (boardId, cardIds) in exportedCardIdsByBoard)
        {
            if (boardId == Guid.Empty)
                continue;
            cancellationToken.ThrowIfCancellationRequested();
            var graph = await _dependencies.GetAsync(boardId, cancellationToken);
            if (graph is null)
                continue;

            relations.AddRange(graph.ReadRelations()
                .Where(edge => cardIds.Contains(edge.SourceCardId) && cardIds.Contains(edge.TargetCardId))
                .Select(edge => new UserDataExportCardRelationDto(
                    boardId, edge.SourceCardId, edge.TargetCardId, edge.RelationType)));
        }
        return relations;
    }

    private async Task<RelationExportScope?> LoadRelationScopeAsync(Guid boardId, CancellationToken cancellationToken)
    {
        if (_dependencies is null || boardId == Guid.Empty)
            return null;
        var graph = await _dependencies.GetAsync(boardId, cancellationToken);
        return graph is null ? null : new RelationExportScope(graph.ReadRelations());
    }

    private sealed class RelationExportScope(IReadOnlyList<CardRelationEdge> relations)
    {
        private readonly IReadOnlyList<CardRelationEdge> _relations = relations;
        private readonly HashSet<Guid> _relationEndpoints = relations
            .SelectMany(edge => new[] { edge.SourceCardId, edge.TargetCardId })
            .ToHashSet();
        private readonly HashSet<Guid> _exportedEndpoints = [];

        public void Observe(Guid cardId)
        {
            if (_relationEndpoints.Contains(cardId))
                _exportedEndpoints.Add(cardId);
        }

        public IEnumerable<CardRelationEdge> ReadExportableRelations() => _relations.Where(edge =>
            _exportedEndpoints.Contains(edge.SourceCardId) && _exportedEndpoints.Contains(edge.TargetCardId));
    }

    private static UserDataExportWorkspaceMemoryDto MapWorkspaceMemory(Domain.Entities.WorkspaceMemory memory) => new(
        memory.Id, memory.BoardId, memory.InsightId, memory.SourceCardId, memory.SourceLayerId,
        memory.SourceDeckRevision, memory.SourceQuestionHash, memory.Title, memory.Text, memory.OriginalText,
        memory.OriginalEvidence, memory.Status, memory.Archived, memory.Revision, memory.CreatedAt, memory.UpdatedAt,
        memory.History.OrderBy(x => x.Revision).Select(x => new UserDataExportWorkspaceMemoryRevisionDto(
            x.Id, x.MemoryId, x.Title, x.Text, x.Status, x.Archived, x.Revision, x.CreatedAt, x.UpdatedAt, x.AnswerSourceAssetId)).ToList(),
        memory.SourceCaptureId, memory.AnswerSourceAssetId, memory.EvidenceSourceAssetId);

    private async Task<SourceStorageExportDto?> BufferSourceStorageAsync(Guid userId, CancellationToken ct)
    {
        if (_sourceStorage is null) return null;
        await using var sourceSnapshot = await _sourceStorage.OpenReadSnapshotAsync(ct);
        long consumed = 0;
        async Task<List<T>> Collect<T>(IAsyncEnumerable<T> rows)
        {
            var result = new List<T>();
            await foreach (var row in rows.WithCancellation(ct))
            {
                consumed += JsonSerializer.SerializeToUtf8Bytes(row, PortabilityJsonOptions).LongLength * 2;
                if (consumed > 25L * 1024 * 1024)
                    throw new DomainException(ErrorCodes.PayloadTooLarge, "The original source export grew beyond its buffer limit; use the streaming export endpoint");
                result.Add(row);
            }
            return result;
        }
        return new(await Collect(_sourceStorage.ObjectsAsync(userId, ct)), await Collect(_sourceStorage.ReferencesAsync(userId, ct)),
            await Collect(_sourceStorage.ChunksAsync(userId, ct)), await Collect(_sourceStorage.RepresentationsAsync(userId, ct)),
            await Collect(_sourceStorage.AudioAnswersAsync(userId, ct)), await Collect(_sourceStorage.AudioTranscriptionAttemptsAsync(userId, ct)),
            await Collect(_sourceStorage.AudioTranscriptionBudgetsAsync(userId, ct)));
    }
    private static async Task WriteSourceRowsAsync<T>(Utf8JsonWriter writer, string name, IAsyncEnumerable<T> rows, CancellationToken ct)
    {
        writer.WriteStartArray(name);
        await foreach (var row in rows.WithCancellation(ct))
        {
            writer.WriteRawValue(JsonSerializer.SerializeToUtf8Bytes(row, PortabilityJsonOptions));
            await writer.FlushAsync(ct);
        }
        writer.WriteEndArray();
    }

    private async IAsyncEnumerable<UserDataExportNativeCaptureDto> StreamNativeCapturesAsync(
        Guid userId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (_captureStore is null) yield break;
        const int pageSize = 100;
        for (var offset = 0; ; offset += pageSize)
        {
            var page = await _captureStore.NativeByUserAsync(userId, pageSize, offset, ct);
            foreach (var capture in page)
            {
                ct.ThrowIfCancellationRequested();
                yield return new(capture.Id, capture.ContextBoardId, MapDurableCapture(capture)!);
            }
            if (page.Count < pageSize) yield break;
        }
    }

    private static UserDataExportQuietInsightDto MapQuietInsight(Domain.Entities.QuietInsight insight) => new(
        insight.Id, insight.BoardId, insight.CardId, insight.MemoryId, insight.Rule, insight.TargetKey,
        insight.Title, insight.Detail, insight.Evidence, insight.State, insight.CheckedAt, insight.SnoozeUntil,
        insight.Revision, insight.CreatedAt, insight.UpdatedAt);

    private async IAsyncEnumerable<Domain.Entities.WorkspaceMemory> StreamWorkspaceMemoriesAsync(
        Guid userId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        for (var offset = 0; ; offset += StreamPageSize)
        {
            var page = await _workspaceInsights.MemoriesByUserAsync(userId, StreamPageSize, offset, ct);
            foreach (var memory in page) { ct.ThrowIfCancellationRequested(); yield return memory; }
            if (page.Count < StreamPageSize) yield break;
        }
    }

    private async IAsyncEnumerable<Domain.Entities.QuietInsight> StreamQuietInsightsAsync(
        Guid userId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        for (var offset = 0; ; offset += StreamPageSize)
        {
            var page = await _workspaceInsights.InsightsByUserAsync(userId, StreamPageSize, offset, ct);
            foreach (var insight in page) { ct.ThrowIfCancellationRequested(); yield return insight; }
            if (page.Count < StreamPageSize) yield break;
        }
    }

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

    private async IAsyncEnumerable<Domain.Entities.Transcript> StreamTranscriptsAsync(
        Guid userId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var offset = 0;
        while (true)
        {
            var page = await _transcripts.GetByUserAsync(
                userId,
                StreamPageSize,
                offset,
                cancellationToken);
            foreach (var transcript in page)
                yield return transcript;

            if (page.Count < StreamPageSize)
                yield break;

            offset += page.Count;
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
