using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class CaptureService : ICaptureService
{
    private const int DefaultListLimit = 50;
    private const int MaxListLimit = 200;
    private const int ExcerptLength = 200;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthorizationService _authorizationService;
    private readonly CaptureIntakeService _captureIntake;
    private readonly ICaptureStore? _captureStore;
    private readonly ICaptureBackfillStore? _backfillStore;
    private readonly ContextFabricSettings _contextFabric;
    private readonly ILogger<CaptureService>? _logger;

    /// <summary>
    /// Memoized read-switch decision for this scope (one Inbox request). Cached rather than
    /// re-queried per item so a page costs one indexed marker lookup, and scoped rather than
    /// process-wide so a host that completes its backfill while running picks the switch up on the
    /// next request instead of after a restart.
    /// </summary>
    private bool? _readThroughStore;

    public CaptureService(
        IUnitOfWork unitOfWork,
        IAuthorizationService authorizationService)
        : this(unitOfWork, authorizationService, captureStore: null, contextFabricSettings: null)
    {
    }

    /// <summary>
    /// The container-resolved constructor. <paramref name="captureStore"/> receives the ID-preserving
    /// mirror of every new capture (with its inline text source asset) through the canonical
    /// <see cref="CaptureIntakeService"/> while <see cref="ContextFabricSettings.DualWriteCaptures"/>
    /// is on (ADR-0065 §Decision 1, CF-01 #2255); with the default settings, or without a store, the
    /// service behaves exactly as before.
    /// </summary>
    public CaptureService(
        IUnitOfWork unitOfWork,
        IAuthorizationService authorizationService,
        ICaptureStore? captureStore,
        ContextFabricSettings? contextFabricSettings)
        : this(unitOfWork, authorizationService, captureStore, contextFabricSettings, backfillStore: null, logger: null)
    {
    }

    /// <summary>
    /// The container-resolved constructor. Adds the backfill marker store, which arms the Inbox read
    /// switch: with the marker complete, a capture's own material (its source text, its capture
    /// source, its intake time) is read from the durable aggregate through
    /// <paramref name="captureStore"/> instead of being parsed out of the queue row's payload JSON.
    /// Without a marker, without a store, or with the flags off, reads stay on the queue row.
    /// </summary>
    public CaptureService(
        IUnitOfWork unitOfWork,
        IAuthorizationService authorizationService,
        ICaptureStore? captureStore,
        ContextFabricSettings? contextFabricSettings,
        ICaptureBackfillStore? backfillStore,
        ILogger<CaptureService>? logger)
    {
        _unitOfWork = unitOfWork;
        _authorizationService = authorizationService;
        _captureStore = captureStore;
        _backfillStore = backfillStore;
        _contextFabric = contextFabricSettings ?? new ContextFabricSettings();
        _logger = logger;
        _captureIntake = new CaptureIntakeService(captureStore, contextFabricSettings);
    }

    /// <summary>
    /// Whether Inbox reads resolve capture material through <see cref="ICaptureStore"/> for this
    /// scope. Armed only when the durable aggregate is being written, the read flag is on, a store
    /// and a marker store are wired, and the ID-preserving backfill has recorded completion on this
    /// database. Anything else keeps the shipped queue-row read path and says so once, because a
    /// host whose backfill has not run must never lose a capture from the Inbox.
    /// </summary>
    private async ValueTask<bool> ReadThroughStoreAsync(CancellationToken cancellationToken)
    {
        if (_readThroughStore.HasValue)
        {
            return _readThroughStore.Value;
        }

        if (!_captureIntake.DualWriteEnabled || !_contextFabric.ReadCapturesFromStore ||
            _captureStore is null || _backfillStore is null)
        {
            _readThroughStore = false;
            _logger?.LogDebug(
                "Context Fabric: Inbox reads stay on the legacy queue row " +
                "(DualWriteCaptures={DualWrite}, ReadCapturesFromStore={ReadFromStore}, store wired={StoreWired}).",
                _contextFabric.DualWriteCaptures,
                _contextFabric.ReadCapturesFromStore,
                _captureStore is not null && _backfillStore is not null);
            return false;
        }

        var state = await _backfillStore.GetStateAsync(
            CaptureBackfillState.LegacyQueueBackfillKey,
            cancellationToken);
        _readThroughStore = state?.IsComplete == true;
        if (!_readThroughStore.Value)
        {
            _logger?.LogInformation(
                "Context Fabric: the ID-preserving capture backfill has not completed on this database, " +
                "so Inbox reads stay on the legacy queue row.");
        }

        return _readThroughStore.Value;
    }

    /// <summary>
    /// Loads the durable captures for a page of queue rows, owner-scoped. Ids with no durable row
    /// are simply absent, and the caller falls back to that row's payload: the read switch never
    /// removes an item from the Inbox.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, Capture>> LoadDurableCapturesAsync(
        Guid userId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0 || !await ReadThroughStoreAsync(cancellationToken))
        {
            return EmptyDurableCaptures;
        }

        var captures = await _captureStore!.GetByIdsForUserAsync(ids, userId, cancellationToken);
        if (captures.Count < ids.Count)
        {
            _logger?.LogDebug(
                "Context Fabric: {Missing} of {Total} Inbox item(s) have no durable capture row yet and " +
                "were read from their queue row.",
                ids.Count - captures.Count,
                ids.Count);
        }

        return captures.ToDictionary(capture => capture.Id);
    }

    private static readonly IReadOnlyDictionary<Guid, Capture> EmptyDurableCaptures =
        new Dictionary<Guid, Capture>();

    private static Capture? Durable(IReadOnlyDictionary<Guid, Capture> lookup, Guid id) =>
        lookup.TryGetValue(id, out var capture) ? capture : null;

    public async Task<Result<CaptureItemDto>> CreateAsync(
        Guid userId,
        CreateCaptureItemDto dto,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<CaptureItemDto>(ErrorCodes.ValidationError, "UserId cannot be empty");

        try
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId, cancellationToken);
            if (user == null)
                return Result.Failure<CaptureItemDto>(ErrorCodes.NotFound, $"User with ID {userId} not found");

            if (dto.BoardId.HasValue)
            {
                var permissionResult = await _authorizationService.CanReadBoardAsync(userId, dto.BoardId.Value);
                if (!permissionResult.IsSuccess)
                    return Result.Failure<CaptureItemDto>(permissionResult.ErrorCode, permissionResult.ErrorMessage);

                if (!permissionResult.Value)
                    return Result.Failure<CaptureItemDto>(ErrorCodes.Forbidden, "You do not have access to this board");
            }

            var sourceResult = ResolveSource(dto.Source);
            if (!sourceResult.IsSuccess)
                return Result.Failure<CaptureItemDto>(sourceResult.ErrorCode, sourceResult.ErrorMessage);

            var payload = new CapturePayloadV1(
                CaptureRequestContract.CurrentSchemaVersion,
                sourceResult.Value,
                dto.Text,
                null,
                dto.TitleHint,
                dto.ExternalRef,
                DueDate: dto.DueDate,
                Labels: dto.Labels);

            var request = new LlmRequest(
                userId,
                CaptureRequestContract.ResolveRequestTypeForSource(sourceResult.Value),
                CaptureRequestContract.SerializePayload(payload),
                dto.BoardId);
            var attributedPayload = CaptureRequestContract.WithProvenance(
                payload,
                request.Id,
                requestedByUserId: userId,
                correlationId: LlmRequestAttributionMapper.ResolveCorrelationIdFromActivity(),
                sourceSurface: LlmRequestAttributionMapper.ResolveSourceSurface(LlmRequestSourceSurface.Capture),
                boardId: dto.BoardId);
            request.UpdatePayload(CaptureRequestContract.SerializePayload(attributedPayload));

            await _unitOfWork.LlmQueue.AddAsync(request, cancellationToken);

            // ADR-0065 Decision 1 / CF-01 (#2255): the canonical intake admits the capture into the
            // durable aggregate under the queue row's own id -- its text and any locator stored as
            // immutable source assets -- staged into the same unit of work as the queue row, so both
            // commit together or not at all. This seam does not know the principal kind beyond what
            // CaptureSourceMapping derives, so it never overrides the mapping's producer.
            var durable = await _captureIntake.IntakeAsync(
                request,
                attributedPayload,
                userId,
                dto.BoardId,
                cancellationToken: cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDetailDto(request, attributedPayload, effectiveBoardId: null, durable));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CaptureItemDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<CaptureItemSummaryDto>>> ListAsync(
        Guid userId,
        CaptureListFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<IReadOnlyList<CaptureItemSummaryDto>>(ErrorCodes.ValidationError, "UserId cannot be empty");

        if (filter.Limit < 0)
            return Result.Failure<IReadOnlyList<CaptureItemSummaryDto>>(ErrorCodes.ValidationError, "Limit cannot be negative");

        var limit = Math.Min(filter.Limit == 0 ? DefaultListLimit : filter.Limit, MaxListLimit);

        // Page the user's captures from the database (newest-first) instead of materializing every
        // request. The raw-board pre-filter is pushed into SQL (#1239): a board-filtered query scans only
        // the target board's captures plus null-board captures (which may still resolve to that board via
        // applied-conversion provenance). The effective-board (provenance) and status filters still need
        // the per-item resolution below, so we keep fetching pages until `limit` matching summaries are
        // collected or the captures are exhausted -- a bound on the first page alone would silently
        // under-fill filtered queries. The common (unfiltered) case is satisfied by a single page.
        var summaries = new List<CaptureItemSummaryDto>(limit);
        var seenIds = new HashSet<Guid>();
        var offset = 0;
        while (summaries.Count < limit)
        {
            var page = (await _unitOfWork.LlmQueue.GetCapturesByUserAsync(userId, limit, offset, filter.BoardId, cancellationToken)).ToList();
            if (page.Count == 0)
            {
                break;
            }
            offset += page.Count;

            var batch = new List<(LlmRequest Item, CapturePayloadV1 Payload)>(page.Count);
            foreach (var item in page)
            {
                // Guard against OFFSET paging re-surfacing a boundary row: if a capture is inserted
                // between page reads, the next OFFSET can return a row already seen on the prior page.
                if (!seenIds.Add(item.Id))
                {
                    continue;
                }

                // The raw-board mismatch (item.BoardId set and != filter.BoardId) is already excluded by
                // the SQL pre-filter above; the effective-board check happens post-provenance below.
                batch.Add((item, ParsePayload(item)));
            }

            if (batch.Count > 0)
            {
                var appliedProposalLookup = await LoadAppliedProposalLookupAsync(batch, cancellationToken);
                var resolvedBatch = new List<(LlmRequest Item, CapturePayloadV1 Payload, Guid? EffectiveBoardId)>(batch.Count);
                foreach (var candidate in batch)
                {
                    var item = candidate.Item;
                    var (effectivePayload, effectiveBoardId, _) = await ResolveAppliedConversionProvenanceAsync(
                        item,
                        candidate.Payload,
                        persistChanges: false,
                        cancellationToken,
                        GetAppliedProposal(appliedProposalLookup, candidate.Payload),
                        allowFallbackLookup: false);

                    resolvedBatch.Add((item, effectivePayload, effectiveBoardId));
                }

                // The unscoped Inbox is the active-work view. Resolve provenance before this lookup so
                // a legacy null-board capture converted onto an archived board is hidden alongside a
                // capture whose raw BoardId points there. Board-filtered reads are the explicit history
                // path and deliberately retain archived artifacts. Load the page's boards in one batch,
                // then let the outer paging loop continue until the requested active limit is filled.
                // ADR-0065 / CF-01 (#2255): resolve this page's capture material through ICaptureStore
                // in one owner-scoped batch. A page item with no durable row keeps its queue-row
                // reading, so the switch can never drop an Inbox item.
                var durableCaptures = await LoadDurableCapturesAsync(
                    userId,
                    resolvedBatch.Select(candidate => candidate.Item.Id).ToList(),
                    cancellationToken);

                var archivedBoardIds = new HashSet<Guid>();
                if (!filter.BoardId.HasValue)
                {
                    var effectiveBoardIds = resolvedBatch
                        .Where(candidate => candidate.EffectiveBoardId.HasValue)
                        .Select(candidate => candidate.EffectiveBoardId!.Value)
                        .Distinct()
                        .ToList();
                    if (effectiveBoardIds.Count > 0)
                    {
                        var effectiveBoards = await _unitOfWork.Boards.GetByIdsAsync(effectiveBoardIds, cancellationToken);
                        archivedBoardIds = effectiveBoards
                            .Where(board => board.IsArchived)
                            .Select(board => board.Id)
                            .ToHashSet();
                    }
                }

                foreach (var candidate in resolvedBatch)
                {
                    var item = candidate.Item;
                    var effectivePayload = candidate.Payload;
                    var effectiveBoardId = candidate.EffectiveBoardId;

                    if (filter.BoardId.HasValue && effectiveBoardId != filter.BoardId.Value)
                    {
                        continue;
                    }

                    if (!filter.BoardId.HasValue &&
                        effectiveBoardId.HasValue &&
                        archivedBoardIds.Contains(effectiveBoardId.Value))
                    {
                        continue;
                    }

                    var summary = MapToSummaryDto(
                        item,
                        effectivePayload,
                        effectiveBoardId,
                        Durable(durableCaptures, item.Id));
                    if (filter.Status.HasValue && summary.Status != filter.Status.Value)
                    {
                        continue;
                    }

                    summaries.Add(summary);
                    if (summaries.Count >= limit)
                    {
                        break;
                    }
                }
            }

            // The repository returned a short page -- the captures are exhausted.
            if (page.Count < limit)
            {
                break;
            }
        }

        return Result.Success<IReadOnlyList<CaptureItemSummaryDto>>(summaries);
    }

    public async Task<Result<CaptureItemDto>> GetByIdAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<CaptureItemDto>(ErrorCodes.ValidationError, "UserId cannot be empty");

        var item = await _unitOfWork.LlmQueue.GetByIdAsync(itemId, cancellationToken);
        if (item == null || !CaptureRequestContract.IsCaptureRequestType(item.RequestType))
            return Result.Failure<CaptureItemDto>(ErrorCodes.NotFound, $"Capture item with ID {itemId} not found");

        if (item.UserId != userId)
            return Result.Failure<CaptureItemDto>(ErrorCodes.Forbidden, "You do not have permission to access this capture item");

        var payload = ParsePayload(item);
        var (effectivePayload, effectiveBoardId, persistedBackfill) = await ResolveAppliedConversionProvenanceAsync(
            item,
            payload,
            persistChanges: true,
            cancellationToken);
        if (persistedBackfill)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var durable = await LoadDurableCaptureAsync(userId, item.Id, cancellationToken);
        return Result.Success(MapToDetailDto(item, effectivePayload, effectiveBoardId, durable));
    }

    /// <summary>Single-item form of <see cref="LoadDurableCapturesAsync"/>; null keeps the queue-row read.</summary>
    private async Task<Capture?> LoadDurableCaptureAsync(
        Guid userId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        if (!await ReadThroughStoreAsync(cancellationToken))
        {
            return null;
        }

        var capture = await _captureStore!.GetByIdForUserAsync(captureId, userId, cancellationToken);
        if (capture is null)
        {
            _logger?.LogDebug(
                "Context Fabric: Inbox item {CaptureId} has no durable capture row yet and was read from its queue row.",
                captureId);
        }

        return capture;
    }

    public Task<Result<CaptureItemDto>> KeepAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default)
        => SetDispositionAsync(userId, itemId, CaptureDisposition.Kept, cancellationToken);

    public Task<Result<CaptureItemDto>> ArchiveAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default)
        => SetDispositionAsync(userId, itemId, CaptureDisposition.Archived, cancellationToken);

    public Task<Result> IgnoreAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        return CancelInternalAsync(userId, itemId, cancellationToken);
    }

    public Task<Result> CancelAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        return CancelInternalAsync(userId, itemId, cancellationToken);
    }

    public Task<Result<CaptureTriageEnqueueResultDto>> EnqueueTriageAsync(
        Guid userId,
        Guid itemId,
        Guid? targetBoardId = null,
        CancellationToken cancellationToken = default)
        => EnqueueTriageAsync(userId, itemId, targetBoardId, boardAccessCache: null, cancellationToken);

    /// <param name="boardAccessCache">
    /// Optional per-call-group memo of board authorization outcomes, keyed by board id. Supplied by
    /// <see cref="BatchTriageAsync"/> so a batch spends one <c>CanWriteBoardAsync</c> lookup per
    /// DISTINCT board instead of one per item (#1836): that lookup is a board fetch plus a
    /// membership read, so a 50-item batch on one board previously paid 50 of each. Board
    /// membership cannot change within a single batch call, so the memo is behaviour-identical;
    /// failure outcomes are memoized too, for the same reason. Null on the single-item path, which
    /// keeps its original one-lookup-per-call shape.
    /// </param>
    private async Task<Result<CaptureTriageEnqueueResultDto>> EnqueueTriageAsync(
        Guid userId,
        Guid itemId,
        Guid? targetBoardId,
        Dictionary<Guid, Result>? boardAccessCache,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
            return Result.Failure<CaptureTriageEnqueueResultDto>(ErrorCodes.ValidationError, "UserId cannot be empty");

        if (targetBoardId.HasValue && targetBoardId.Value == Guid.Empty)
            return Result.Failure<CaptureTriageEnqueueResultDto>(ErrorCodes.ValidationError, "BoardId cannot be empty");

        var item = await _unitOfWork.LlmQueue.GetByIdAsync(itemId, cancellationToken);
        if (item == null || !CaptureRequestContract.IsCaptureRequestType(item.RequestType))
            return Result.Failure<CaptureTriageEnqueueResultDto>(ErrorCodes.NotFound, $"Capture item with ID {itemId} not found");

        if (item.UserId != userId)
            return Result.Failure<CaptureTriageEnqueueResultDto>(ErrorCodes.Forbidden, "You do not have permission to modify this capture item");

        var payload = ParsePayload(item);
        var (effectivePayload, effectiveBoardId, persistedBackfill) = await ResolveAppliedConversionProvenanceAsync(
            item,
            payload,
            persistChanges: true,
            cancellationToken);
        if (persistedBackfill)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var currentStatus = ResolveCaptureStatus(item, effectivePayload);
        if (currentStatus == CaptureStatus.Triaging)
        {
            // Already in flight — the board was resolved on the first accept. Stay idempotent so a
            // double-click can't turn a live triage into a spurious validation error.
            return Result.Success(new CaptureTriageEnqueueResultDto(
                item.Id,
                CaptureStatus.Triaging,
                AlreadyTriaging: true));
        }

        if (!CaptureStatusPolicy.CanTransition(currentStatus, CaptureStatus.Triaging))
        {
            return Result.Failure<CaptureTriageEnqueueResultDto>(
                ErrorCodes.Conflict,
                $"Capture item cannot transition from {currentStatus} to {CaptureStatus.Triaging}");
        }

        if (item.Status != RequestStatus.Pending && item.Status != RequestStatus.Failed)
        {
            return Result.Failure<CaptureTriageEnqueueResultDto>(
                ErrorCodes.Conflict,
                $"Capture item cannot transition from {currentStatus} to {CaptureStatus.Triaging}");
        }

        // A capture with no board can never be triaged into a proposal — a proposal targets a board
        // (CaptureTriageService enforces this at the worker). Home quick-capture lands board-less, so
        // resolve the target board now that the item is otherwise transition-eligible: link a
        // caller-supplied board, then reject synchronously with a 400 instead of queueing a doomed
        // async job that dead-ends at a bare FAILED badge with no reason (#1764).
        if (!effectiveBoardId.HasValue && targetBoardId.HasValue)
        {
            // Gate the link on write access, not read (#1794): the board is only being attached so a
            // proposal can be queued against it, so the same bar applies as to an already-linked board.
            var linkPermission = await EnsureBoardProposalAccessAsync(userId, targetBoardId.Value, boardAccessCache);
            if (!linkPermission.IsSuccess)
                return Result.Failure<CaptureTriageEnqueueResultDto>(linkPermission.ErrorCode, linkPermission.ErrorMessage);

            effectiveBoardId = targetBoardId;
        }
        else if (effectiveBoardId.HasValue)
        {
            // Same gate for a capture that already carries its board: the read-only injection vector
            // is reachable through capture-with-board + accept, not only through the backfill body.
            var boardPermission = await EnsureBoardProposalAccessAsync(userId, effectiveBoardId.Value, boardAccessCache);
            if (!boardPermission.IsSuccess)
                return Result.Failure<CaptureTriageEnqueueResultDto>(boardPermission.ErrorCode, boardPermission.ErrorMessage);
        }

        if (!effectiveBoardId.HasValue)
        {
            return Result.Failure<CaptureTriageEnqueueResultDto>(
                ErrorCodes.ValidationError,
                "Choose a board before accepting this capture. Triage turns a capture into a board proposal, so it needs a target board.");
        }

        try
        {
            var expectedStatus = item.Status;
            var expectedUpdatedAt = item.UpdatedAt;
            if (!item.BoardId.HasValue)
            {
                item.BackfillBoard(effectiveBoardId.Value);
            }
            if (item.Status == RequestStatus.Failed)
            {
                item.ResetForRetry();
            }

            if (effectivePayload.Disposition?.Kind != CaptureDisposition.ProposalRequested)
            {
                effectivePayload = effectivePayload with
                {
                    Disposition = new CaptureDispositionV1(
                        CaptureDisposition.ProposalRequested,
                        DateTimeOffset.UtcNow,
                        userId,
                        effectiveBoardId)
                };
            }

            item.UpdatePayload(CaptureRequestContract.SerializePayload(effectivePayload));
            item.MarkAsProcessing();
            var enqueued = await _unitOfWork.LlmQueue.TryEnqueueCaptureTriageAsync(
                item.Id,
                expectedStatus,
                expectedUpdatedAt,
                item.Payload,
                effectiveBoardId.Value,
                cancellationToken);
            if (!enqueued)
            {
                return Result.Failure<CaptureTriageEnqueueResultDto>(
                    ErrorCodes.Conflict,
                    "Capture item changed while triage was being requested");
            }

            return Result.Success(new CaptureTriageEnqueueResultDto(
                item.Id,
                CaptureStatus.Triaging,
                AlreadyTriaging: false));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CaptureTriageEnqueueResultDto>(ErrorCodes.Conflict, ex.Message);
        }
    }

    /// <summary>
    /// Board-targeted triage generates an automation proposal into the target board's review queue,
    /// so it requires write-capable membership on that board — the roles
    /// <see cref="BoardAccess.CanWrite"/> admits (Owner, Admin, Editor), plus the board owner. A
    /// Viewer can read the board but must not be able to inject proposals into a queue only
    /// approvers can clear (#1794). Approval and execution authorization are unchanged: write access
    /// buys the right to <em>suggest</em>; every board mutation still needs an explicit approve and
    /// execute by an approver.
    /// </summary>
    private async Task<Result> EnsureBoardProposalAccessAsync(
        Guid userId,
        Guid boardId,
        Dictionary<Guid, Result>? boardAccessCache = null)
    {
        if (boardAccessCache is not null && boardAccessCache.TryGetValue(boardId, out var memoized))
            return memoized;

        var permission = await _authorizationService.CanWriteBoardAsync(userId, boardId);
        var outcome = !permission.IsSuccess
            ? Result.Failure(permission.ErrorCode, permission.ErrorMessage)
            : permission.Value
                ? Result.Success()
                : Result.Failure(ErrorCodes.Forbidden, BoardProposalAccessDeniedMessage);

        if (boardAccessCache is not null)
            boardAccessCache[boardId] = outcome;

        return outcome;
    }

    private const string BoardProposalAccessDeniedMessage =
        "You do not have permission to modify this board. Triaging a capture into a board queues an " +
        "automation proposal there, which requires write access to that board.";

    private static readonly HashSet<string> ValidBatchActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "triage", "ignore", "cancel"
    };

    private const int MaxBatchSize = 50;

    public async Task<Result<BatchTriageResultDto>> BatchTriageAsync(
        Guid userId,
        BatchTriageRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<BatchTriageResultDto>(ErrorCodes.ValidationError, "UserId cannot be empty");

        if (request.Items == null || request.Items.Count == 0)
            return Result.Failure<BatchTriageResultDto>(ErrorCodes.ValidationError, "At least one item is required");

        if (request.Items.Count > MaxBatchSize)
            return Result.Failure<BatchTriageResultDto>(ErrorCodes.ValidationError, $"Batch size cannot exceed {MaxBatchSize}");

        var duplicateIds = request.Items
            .GroupBy(i => i.ItemId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateIds.Count > 0)
            return Result.Failure<BatchTriageResultDto>(ErrorCodes.ValidationError, "Duplicate item IDs in batch request");

        var invalidActions = request.Items
            .Where(i => !ValidBatchActions.Contains(i.Action))
            .ToList();
        if (invalidActions.Count > 0)
            return Result.Failure<BatchTriageResultDto>(ErrorCodes.ValidationError,
                $"Invalid action(s): {string.Join(", ", invalidActions.Select(i => i.Action))}. Valid actions: triage, ignore, cancel");

        var results = new List<BatchTriageItemResultDto>(request.Items.Count);

        // One board-authorization lookup per DISTINCT board for the whole batch, not one per item
        // (#1836). Shared across every triage item below; see the boardAccessCache parameter on
        // EnqueueTriageAsync for why memoizing inside a single batch is behaviour-identical.
        var boardAccessCache = new Dictionary<Guid, Result>();

        foreach (var itemAction in request.Items)
        {
            try
            {
                var actionResult = itemAction.Action.ToLowerInvariant() switch
                {
                    "triage" => await ExecuteBatchItemTriageAsync(userId, itemAction.ItemId, boardAccessCache, cancellationToken),
                    "ignore" => await CancelInternalAsync(userId, itemAction.ItemId, cancellationToken),
                    "cancel" => await CancelInternalAsync(userId, itemAction.ItemId, cancellationToken),
                    _ => Result.Failure(ErrorCodes.ValidationError, $"Unknown action: {itemAction.Action}")
                };

                results.Add(new BatchTriageItemResultDto(
                    itemAction.ItemId,
                    actionResult.IsSuccess,
                    actionResult.IsSuccess ? null : actionResult.ErrorCode,
                    actionResult.IsSuccess ? null : actionResult.ErrorMessage));
            }
            catch (Exception)
            {
                results.Add(new BatchTriageItemResultDto(
                    itemAction.ItemId,
                    false,
                    ErrorCodes.UnexpectedError,
                    "An unexpected error occurred while processing this item"));
            }
        }

        var succeeded = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success);

        return Result.Success(new BatchTriageResultDto(
            results.Count,
            succeeded,
            failed,
            results));
    }

    private async Task<Result> ExecuteBatchItemTriageAsync(
        Guid userId,
        Guid itemId,
        Dictionary<Guid, Result> boardAccessCache,
        CancellationToken cancellationToken)
    {
        var triageResult = await EnqueueTriageAsync(userId, itemId, targetBoardId: null, boardAccessCache, cancellationToken);
        return triageResult.IsSuccess
            ? Result.Success()
            : Result.Failure(triageResult.ErrorCode, triageResult.ErrorMessage);
    }

    public async Task<Result<CaptureItemDto>> UpdateSuggestionAsync(
        Guid userId,
        Guid itemId,
        UpdateCaptureSuggestionDto dto,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<CaptureItemDto>(ErrorCodes.ValidationError, "UserId cannot be empty");

        if (string.IsNullOrWhiteSpace(dto.Text))
            return Result.Failure<CaptureItemDto>(ErrorCodes.ValidationError, "Text cannot be empty");

        if (dto.TitleHint != null && dto.TitleHint.Length > CaptureRequestContract.MaxTitleHintLength)
            return Result.Failure<CaptureItemDto>(ErrorCodes.ValidationError,
                $"Title hint exceeds maximum length of {CaptureRequestContract.MaxTitleHintLength} characters");

        var item = await _unitOfWork.LlmQueue.GetByIdAsync(itemId, cancellationToken);
        if (item == null || !CaptureRequestContract.IsCaptureRequestType(item.RequestType))
            return Result.Failure<CaptureItemDto>(ErrorCodes.NotFound, $"Capture item with ID {itemId} not found");

        if (item.UserId != userId)
            return Result.Failure<CaptureItemDto>(ErrorCodes.Forbidden, "You do not have permission to modify this capture item");

        if (item.TranscriptId.HasValue)
        {
            return Result.Failure<CaptureItemDto>(
                ErrorCodes.Conflict,
                "Capture text cannot be edited after its transcript is linked");
        }

        var currentPayload = ParsePayload(item);
        var currentStatus = ResolveCaptureStatus(item, currentPayload);

        if (!CanEditSuggestion(item, currentStatus))
        {
            return Result.Failure<CaptureItemDto>(ErrorCodes.Conflict,
                $"Capture item in status {currentStatus} cannot be edited");
        }

        var maxTextLength = CaptureRequestContract.IsTranscriptSource(currentPayload.Source)
            ? CaptureRequestContract.MaxTranscriptTextLength
            : CaptureRequestContract.MaxRawTextLength;
        if (dto.Text.Length > maxTextLength)
            return Result.Failure<CaptureItemDto>(ErrorCodes.ValidationError,
                $"Text exceeds maximum length of {maxTextLength} characters");

        var updatedPayload = new CapturePayloadV1(
            currentPayload.Version,
            currentPayload.Source,
            dto.Text,
            currentPayload.ClientCreatedAt,
            dto.TitleHint ?? currentPayload.TitleHint,
            currentPayload.ExternalRef,
            currentPayload.Provenance,
            DueDate: dto.Metadata == null ? currentPayload.DueDate : dto.Metadata.DueDate,
            Labels: dto.Metadata == null
                ? currentPayload.Labels
                : dto.Metadata.Labels ?? Array.Empty<string>(),
            Disposition: currentPayload.Disposition);

        var payloadValidation = CaptureRequestContract.ValidatePayload(updatedPayload);
        if (!payloadValidation.IsSuccess)
        {
            return Result.Failure<CaptureItemDto>(
                payloadValidation.ErrorCode,
                payloadValidation.ErrorMessage);
        }

        item.UpdatePayload(CaptureRequestContract.SerializePayload(updatedPayload));

        // ADR-0065 / CF-01 (#2255): sources are immutable, so a post-intake edit appends a
        // SUPERSEDING inline text asset and leaves the original readable -- it never rewrites the
        // stored bytes. The record of what the user first typed or pasted survives every correction,
        // and a representation can still name the exact asset it was derived from. Staged into the
        // same unit of work as the queue row, so the edit and the new source commit together.
        var durable = await SupersedeDurableTextAsync(userId, item.Id, dto.Text, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(MapToDetailDto(item, updatedPayload, effectiveBoardId: null, durable));
    }

    /// <summary>
    /// Appends the corrected text as a superseding <c>SourceAsset</c> on the durable capture, if
    /// there is one. Returns the mutated aggregate so the caller's DTO reflects the new current
    /// text; null when the capture is not (yet) durable, which leaves the queue-row reading intact.
    /// </summary>
    private async Task<Capture?> SupersedeDurableTextAsync(
        Guid userId,
        Guid captureId,
        string text,
        CancellationToken cancellationToken)
    {
        if (_captureStore is null || !_captureIntake.DualWriteEnabled)
        {
            return null;
        }

        var capture = await _captureStore.GetByIdForUpdateAsync(captureId, userId, cancellationToken);
        if (capture is null)
        {
            return null;
        }

        try
        {
            capture.SupersedeInlineTextSource(text);
        }
        catch (DomainException ex)
        {
            // The durable side must never be the reason an operation the queue row accepted fails.
            _logger?.LogWarning(
                ex,
                "Context Fabric: could not record a superseding source for capture {CaptureId}; " +
                "the edit still applied to the queue row.",
                captureId);
            return null;
        }

        await _captureStore.UpdateAsync(capture, cancellationToken);
        return capture;
    }

    private async Task<Result> CancelInternalAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
            return Result.Failure(ErrorCodes.ValidationError, "UserId cannot be empty");

        var item = await _unitOfWork.LlmQueue.GetByIdAsync(itemId, cancellationToken);
        if (item == null || !CaptureRequestContract.IsCaptureRequestType(item.RequestType))
            return Result.Failure(ErrorCodes.NotFound, $"Capture item with ID {itemId} not found");

        if (item.UserId != userId)
            return Result.Failure(ErrorCodes.Forbidden, "You do not have permission to modify this capture item");

        if (item.Status == RequestStatus.Cancelled)
            return Result.Success();

        try
        {
            item.Cancel();

            // The user's disposition is a durable column now, not JSON on the queue row: putting a
            // capture away records Archived on the aggregate's disposition axis in the same unit of
            // work. Processing and action outcomes are deliberately left standing -- archiving is a
            // decision about the Inbox, not an erasure of what was produced (ADR-0065 Decision 1).
            await ApplyDurableDispositionAsync(userId, item.Id, CaptureDisposition.Archived, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.ErrorCode, ex.Message);
        }
    }

    /// <summary>
    /// Records the user's disposition on the durable aggregate's own axis. Returns the mutated
    /// aggregate, or null when the capture is not durable (nothing to record) - never an error: a
    /// disposition the queue row accepted must not fail because the mirror is behind.
    /// </summary>
    private async Task<Capture?> ApplyDurableDispositionAsync(
        Guid userId,
        Guid captureId,
        CaptureDisposition disposition,
        CancellationToken cancellationToken)
    {
        if (_captureStore is null || !_captureIntake.DualWriteEnabled)
        {
            return null;
        }

        var capture = await _captureStore.GetByIdForUpdateAsync(captureId, userId, cancellationToken);
        if (capture is null)
        {
            return null;
        }

        try
        {
            switch (CaptureUserDispositionMapping.FromLegacy(disposition))
            {
                case CaptureUserDisposition.Archived:
                    capture.Archive();
                    break;
                case CaptureUserDisposition.Kept:
                    capture.Keep();
                    break;
                default:
                    capture.Reactivate();
                    break;
            }
        }
        catch (DomainException ex)
        {
            // Same rule as every other durable write on a shipped path: never fail where the queue
            // row succeeded. The queue row has already been updated by this point.
            _logger?.LogWarning(
                ex,
                "Context Fabric: could not record disposition {Disposition} on capture {CaptureId}; " +
                "the queue row still carries it.",
                disposition,
                captureId);
            return null;
        }

        await _captureStore.UpdateAsync(capture, cancellationToken);
        return capture;
    }

    private async Task<Result<CaptureItemDto>> SetDispositionAsync(
        Guid userId,
        Guid itemId,
        CaptureDisposition disposition,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
            return Result.Failure<CaptureItemDto>(ErrorCodes.ValidationError, "UserId cannot be empty");

        var item = await _unitOfWork.LlmQueue.GetByIdAsync(itemId, cancellationToken);
        if (item == null || !CaptureRequestContract.IsCaptureRequestType(item.RequestType))
            return Result.Failure<CaptureItemDto>(ErrorCodes.NotFound, $"Capture item with ID {itemId} not found");

        if (item.UserId != userId)
            return Result.Failure<CaptureItemDto>(ErrorCodes.Forbidden, "You do not have permission to modify this capture item");

        var payload = ParsePayload(item);
        var status = ResolveCaptureStatus(item, payload);

        if (payload.Disposition?.Kind == disposition &&
            (status is CaptureStatus.New or CaptureStatus.Failed ||
             disposition == CaptureDisposition.Archived && status == CaptureStatus.Ignored))
        {
            return Result.Success(MapToDetailDto(item, payload));
        }

        if (status is not CaptureStatus.New and not CaptureStatus.Failed)
        {
            return Result.Failure<CaptureItemDto>(
                ErrorCodes.Conflict,
                $"Capture item cannot be {disposition.ToString().ToLowerInvariant()} from {status}");
        }

        var existingProposal = await _unitOfWork.AutomationProposals.GetBySourceReferenceAsync(
            ProposalSourceType.Queue,
            item.Id.ToString(),
            cancellationToken);
        if (existingProposal?.Status is ProposalStatus.PendingReview or ProposalStatus.Approved or ProposalStatus.Applied)
        {
            return Result.Failure<CaptureItemDto>(
                ErrorCodes.Conflict,
                "Capture item already has a proposal in review or applied work");
        }

        try
        {
            var expectedStatus = item.Status;
            var expectedUpdatedAt = item.UpdatedAt;
            var updatedPayload = payload with
            {
                Disposition = new CaptureDispositionV1(
                    disposition,
                    DateTimeOffset.UtcNow,
                    userId,
                    item.BoardId)
            };
            var targetStatus = disposition == CaptureDisposition.Archived
                ? RequestStatus.Cancelled
                : item.Status;
            if (disposition == CaptureDisposition.Archived)
            {
                item.Cancel();
            }
            item.UpdatePayload(CaptureRequestContract.SerializePayload(updatedPayload));
            var updated = await _unitOfWork.LlmQueue.TrySetCaptureDispositionAsync(
                item.Id,
                expectedStatus,
                expectedUpdatedAt,
                targetStatus,
                item.Payload,
                cancellationToken);
            if (!updated)
            {
                return Result.Failure<CaptureItemDto>(
                    ErrorCodes.Conflict,
                    "Capture item changed while its disposition was being recorded");
            }

            // Only after the conditional queue-row update actually won: a lost race must not leave
            // the durable disposition axis ahead of the row it describes.
            var durable = await ApplyDurableDispositionAsync(userId, item.Id, disposition, cancellationToken);
            if (durable is not null)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result.Success(MapToDetailDto(item, updatedPayload, effectiveBoardId: null, durable));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CaptureItemDto>(ex.ErrorCode, ex.Message);
        }
    }

    private static Result<CaptureSource> ResolveSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return Result.Success(CaptureSource.Typed);

        var normalized = source.Trim();
        if (Enum.TryParse<CaptureSource>(normalized, true, out var parsedSource) &&
            Enum.IsDefined(typeof(CaptureSource), parsedSource))
        {
            return Result.Success(parsedSource);
        }

        return Result.Failure<CaptureSource>(ErrorCodes.ValidationError, "Invalid capture source value");
    }

    /// <summary>
    /// The capture material the read path serves, resolved from the durable aggregate when one
    /// exists and from the queue row's payload otherwise (ADR-0065; CF-01 <c>#2255</c>).
    /// <para>
    /// <b>What the aggregate owns.</b> The immutable source text (its newest asset that nothing has
    /// superseded), the capture source snapshot and the server intake time - the three things the
    /// Inbox used to obtain by parsing <c>LlmRequest.Payload</c>. Everything else the DTOs carry is
    /// still job state (queue status, processed-at, retry count, error message) or has no column
    /// yet (triage provenance, suggestion metadata, the disposition receipt's who/when/where); those
    /// keep their shipped source until the slices that own them land, which is what keeps the DTOs
    /// byte-identical across the switch.
    /// </para>
    /// </summary>
    private static (string Text, CaptureSource Source, DateTimeOffset CreatedAt) ResolveCaptureMaterial(
        LlmRequest item,
        CapturePayloadV1 payload,
        Capture? durable)
    {
        if (durable is null)
        {
            return (payload.Text, payload.Source, item.CreatedAt);
        }

        return (
            durable.CurrentText ?? payload.Text,
            durable.LegacySourceSnapshot,
            durable.CapturedAtServer);
    }

    private static CaptureItemSummaryDto MapToSummaryDto(
        LlmRequest item,
        CapturePayloadV1 payload,
        Guid? effectiveBoardId = null,
        Capture? durable = null)
    {
        var material = ResolveCaptureMaterial(item, payload, durable);
        var excerpt = BuildExcerpt(material.Text);
        var status = ResolveCaptureStatus(item, payload);

        return new CaptureItemSummaryDto(
            item.Id,
            item.UserId,
            effectiveBoardId ?? item.BoardId,
            status,
            material.Source,
            excerpt,
            material.CreatedAt,
            item.ProcessedAt,
            item.ErrorMessage,
            payload.Disposition);
    }

    private static CaptureItemDto MapToDetailDto(
        LlmRequest item,
        CapturePayloadV1 payload,
        Guid? effectiveBoardId = null,
        Capture? durable = null)
    {
        var material = ResolveCaptureMaterial(item, payload, durable);
        var excerpt = BuildExcerpt(material.Text);
        var status = ResolveCaptureStatus(item, payload);

        return new CaptureItemDto(
            item.Id,
            item.UserId,
            effectiveBoardId ?? item.BoardId,
            status,
            material.Source,
            material.Text,
            excerpt,
            material.CreatedAt,
            item.ProcessedAt,
            item.RetryCount,
            item.ErrorMessage,
            payload.Provenance,
            CanEditSuggestion(item, status),
            new CaptureSuggestionMetadataDto(
                payload.DueDate,
                payload.Labels ?? Array.Empty<string>()),
            payload.Disposition);
    }

    private static bool CanEditSuggestion(LlmRequest item, CaptureStatus status) =>
        !item.TranscriptId.HasValue && IsSuggestionEditableStatus(status);

    private static bool IsSuggestionEditableStatus(CaptureStatus status) =>
        status is CaptureStatus.New or CaptureStatus.Failed or CaptureStatus.Triaged;

    private async Task<IReadOnlyDictionary<Guid, AutomationProposal>> LoadAppliedProposalLookupAsync(
        IReadOnlyList<(LlmRequest Item, CapturePayloadV1 Payload)> captureItems,
        CancellationToken cancellationToken)
    {
        var proposalIds = captureItems
            .Select(candidate => candidate.Payload.Provenance?.ProposalId)
            .Where(proposalId => proposalId.HasValue && proposalId.Value != Guid.Empty)
            .Select(proposalId => proposalId!.Value)
            .Distinct()
            .ToList();

        if (proposalIds.Count == 0)
        {
            return new Dictionary<Guid, AutomationProposal>();
        }

        var proposals = await _unitOfWork.AutomationProposals.GetByIdsAsync(proposalIds, cancellationToken);
        if (proposals == null)
        {
            return new Dictionary<Guid, AutomationProposal>();
        }

        return proposals.ToDictionary(proposal => proposal.Id);
    }

    private static AutomationProposal? GetAppliedProposal(
        IReadOnlyDictionary<Guid, AutomationProposal> proposalLookup,
        CapturePayloadV1 payload)
    {
        var proposalId = payload.Provenance?.ProposalId;
        if (!proposalId.HasValue || proposalId.Value == Guid.Empty)
        {
            return null;
        }

        return proposalLookup.TryGetValue(proposalId.Value, out var proposal)
            ? proposal
            : null;
    }

    private async Task<(CapturePayloadV1 Payload, Guid? EffectiveBoardId, bool PersistedBackfill)> ResolveAppliedConversionProvenanceAsync(
        LlmRequest item,
        CapturePayloadV1 payload,
        bool persistChanges,
        CancellationToken cancellationToken,
        AutomationProposal? preloadedProposal = null,
        bool allowFallbackLookup = true)
    {
        // Server-stamped provenance is the authoritative legacy fallback when the raw request's
        // BoardId was never backfilled. Prefer the raw FK when both exist; a client cannot inject
        // this attribution because capture contract parsing rejects client-supplied provenance.
        var storedEffectiveBoardId = CaptureEffectiveBoardPolicy.ResolveEffectiveBoardId(
            item.Id,
            item.UserId,
            item.BoardId,
            payload.Provenance?.BoardId,
            payload.Provenance?.ProposalId,
            payload.Provenance?.ConvertedAt);
        var proposalId = payload.Provenance?.ProposalId;
        if (!proposalId.HasValue ||
            proposalId.Value == Guid.Empty ||
            payload.Provenance?.ConvertedAt is not null)
        {
            return (payload, storedEffectiveBoardId, false);
        }

        var proposal = preloadedProposal;
        if ((proposal == null || proposal.Id != proposalId.Value) && allowFallbackLookup)
        {
            proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(proposalId.Value, cancellationToken);
        }

        if (!CaptureEffectiveBoardPolicy.IsValidatedAppliedProposal(
                item.Id,
                item.UserId,
                proposalId,
                payload.Provenance?.ConvertedAt,
                proposal))
        {
            return (payload, storedEffectiveBoardId, false);
        }

        var validatedProposal = proposal!;
        var resolvedBoardId = CaptureEffectiveBoardPolicy.ResolveEffectiveBoardId(
            item.Id,
            item.UserId,
            item.BoardId,
            payload.Provenance?.BoardId,
            proposalId,
            payload.Provenance?.ConvertedAt,
            validatedProposal);
        var convertedPayload = CaptureRequestContract.WithProvenance(
            payload,
            item.Id,
            proposalId: validatedProposal.Id,
            boardId: resolvedBoardId,
            convertedAt: CaptureConversionTimestamp.ResolveConvertedAt(validatedProposal.AppliedAt));

        if (!persistChanges)
        {
            return (convertedPayload, resolvedBoardId, false);
        }

        if (!item.BoardId.HasValue && resolvedBoardId.HasValue)
        {
            item.BackfillBoard(resolvedBoardId.Value);
        }

        item.UpdatePayload(CaptureRequestContract.SerializePayload(convertedPayload));
        return (convertedPayload, item.BoardId, true);
    }

    private static CapturePayloadV1 ParsePayload(LlmRequest item)
    {
        return CaptureRequestContract.ParseStoredPayload(item.Payload);
    }

    private static CaptureStatus ResolveCaptureStatus(LlmRequest item, CapturePayloadV1 payload)
    {
        var hasLinkedProposal = payload.Provenance?.ProposalId is { } proposalId &&
                                proposalId != Guid.Empty;
        var isConverted = payload.Provenance?.ConvertedAt is not null;
        return CaptureStatusPolicy.MapFromQueueStatus(item.Status, hasLinkedProposal, isConverted);
    }

    private static string BuildExcerpt(string rawText)
    {
        var normalized = string.Join(
            " ",
            rawText
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length <= ExcerptLength)
            return normalized;

        return normalized[..ExcerptLength];
    }
}
