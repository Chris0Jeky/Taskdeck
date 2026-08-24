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

    public CaptureService(
        IUnitOfWork unitOfWork,
        IAuthorizationService authorizationService)
    {
        _unitOfWork = unitOfWork;
        _authorizationService = authorizationService;
    }

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
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDetailDto(request, attributedPayload));
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

                    var summary = MapToSummaryDto(item, effectivePayload, effectiveBoardId);
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

        return Result.Success(MapToDetailDto(item, effectivePayload, effectiveBoardId));
    }

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

            try
            {
                item.BackfillBoard(targetBoardId.Value);
            }
            catch (DomainException ex)
            {
                return Result.Failure<CaptureTriageEnqueueResultDto>(ex.ErrorCode, ex.Message);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
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
            if (item.Status == RequestStatus.Failed)
            {
                item.ResetForRetry();
            }

            item.MarkAsProcessing();
            await _unitOfWork.SaveChangesAsync(cancellationToken);

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
                : dto.Metadata.Labels ?? Array.Empty<string>());

        var payloadValidation = CaptureRequestContract.ValidatePayload(updatedPayload);
        if (!payloadValidation.IsSuccess)
        {
            return Result.Failure<CaptureItemDto>(
                payloadValidation.ErrorCode,
                payloadValidation.ErrorMessage);
        }

        item.UpdatePayload(CaptureRequestContract.SerializePayload(updatedPayload));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(MapToDetailDto(item, updatedPayload));
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
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.ErrorCode, ex.Message);
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

    private static CaptureItemSummaryDto MapToSummaryDto(LlmRequest item, CapturePayloadV1 payload, Guid? effectiveBoardId = null)
    {
        var excerpt = BuildExcerpt(payload.Text);
        var status = ResolveCaptureStatus(item, payload);

        return new CaptureItemSummaryDto(
            item.Id,
            item.UserId,
            effectiveBoardId ?? item.BoardId,
            status,
            payload.Source,
            excerpt,
            item.CreatedAt,
            item.ProcessedAt,
            item.ErrorMessage);
    }

    private static CaptureItemDto MapToDetailDto(LlmRequest item, CapturePayloadV1 payload, Guid? effectiveBoardId = null)
    {
        var excerpt = BuildExcerpt(payload.Text);
        var status = ResolveCaptureStatus(item, payload);

        return new CaptureItemDto(
            item.Id,
            item.UserId,
            effectiveBoardId ?? item.BoardId,
            status,
            payload.Source,
            payload.Text,
            excerpt,
            item.CreatedAt,
            item.ProcessedAt,
            item.RetryCount,
            item.ErrorMessage,
            payload.Provenance,
            CanEditSuggestion(item, status),
            new CaptureSuggestionMetadataDto(
                payload.DueDate,
                payload.Labels ?? Array.Empty<string>()));
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
