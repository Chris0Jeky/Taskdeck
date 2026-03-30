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
                dto.ExternalRef);

            var request = new LlmRequest(
                userId,
                CaptureRequestContract.RequestTypeV1,
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

        var items = await _unitOfWork.LlmQueue.GetByUserAsync(userId, cancellationToken);
        var captureItems = items
            .Where(item => CaptureRequestContract.IsCaptureRequestType(item.RequestType))
            .OrderByDescending(item => item.CreatedAt)
            .ToList();

        var summaries = new List<CaptureItemSummaryDto>(limit);
        for (var startIndex = 0; startIndex < captureItems.Count && summaries.Count < limit;)
        {
            var remaining = limit - summaries.Count;
            var batchSize = Math.Max(remaining, 1);
            var batch = new List<(LlmRequest Item, CapturePayloadV1 Payload)>(batchSize);

            while (startIndex < captureItems.Count && batch.Count < batchSize)
            {
                var item = captureItems[startIndex++];
                if (filter.BoardId.HasValue && item.BoardId.HasValue && item.BoardId != filter.BoardId.Value)
                {
                    continue;
                }

                batch.Add((item, ParsePayload(item)));
            }

            if (batch.Count == 0)
            {
                continue;
            }

            var appliedProposalLookup = await LoadAppliedProposalLookupAsync(batch, cancellationToken);
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

                if (filter.BoardId.HasValue && effectiveBoardId != filter.BoardId.Value)
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

    public async Task<Result<CaptureTriageEnqueueResultDto>> EnqueueTriageAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<CaptureTriageEnqueueResultDto>(ErrorCodes.ValidationError, "UserId cannot be empty");

        var item = await _unitOfWork.LlmQueue.GetByIdAsync(itemId, cancellationToken);
        if (item == null || !CaptureRequestContract.IsCaptureRequestType(item.RequestType))
            return Result.Failure<CaptureTriageEnqueueResultDto>(ErrorCodes.NotFound, $"Capture item with ID {itemId} not found");

        if (item.UserId != userId)
            return Result.Failure<CaptureTriageEnqueueResultDto>(ErrorCodes.Forbidden, "You do not have permission to modify this capture item");

        var payload = ParsePayload(item);
        var (effectivePayload, _, persistedBackfill) = await ResolveAppliedConversionProvenanceAsync(
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

        foreach (var itemAction in request.Items)
        {
            try
            {
                var actionResult = itemAction.Action.ToLowerInvariant() switch
                {
                    "triage" => await ExecuteBatchItemTriageAsync(userId, itemAction.ItemId, cancellationToken),
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
        CancellationToken cancellationToken)
    {
        var triageResult = await EnqueueTriageAsync(userId, itemId, cancellationToken);
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

        var item = await _unitOfWork.LlmQueue.GetByIdAsync(itemId, cancellationToken);
        if (item == null || !CaptureRequestContract.IsCaptureRequestType(item.RequestType))
            return Result.Failure<CaptureItemDto>(ErrorCodes.NotFound, $"Capture item with ID {itemId} not found");

        if (item.UserId != userId)
            return Result.Failure<CaptureItemDto>(ErrorCodes.Forbidden, "You do not have permission to modify this capture item");

        var currentPayload = ParsePayload(item);
        var currentStatus = ResolveCaptureStatus(item, currentPayload);

        if (currentStatus != CaptureStatus.New && currentStatus != CaptureStatus.Failed &&
            currentStatus != CaptureStatus.Triaged)
        {
            return Result.Failure<CaptureItemDto>(ErrorCodes.Conflict,
                $"Capture item in status {currentStatus} cannot be edited");
        }

        var updatedPayload = new CapturePayloadV1(
            currentPayload.Version,
            currentPayload.Source,
            dto.Text,
            null,
            dto.TitleHint ?? currentPayload.TitleHint,
            currentPayload.ExternalRef,
            currentPayload.Provenance);

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
            item.ProcessedAt);
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
            payload.Provenance);
    }

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
        var proposalId = payload.Provenance?.ProposalId;
        if (!proposalId.HasValue ||
            proposalId.Value == Guid.Empty ||
            payload.Provenance?.ConvertedAt is not null)
        {
            return (payload, item.BoardId, false);
        }

        var proposal = preloadedProposal;
        if ((proposal == null || proposal.Id != proposalId.Value) && allowFallbackLookup)
        {
            proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(proposalId.Value, cancellationToken);
        }

        if (proposal == null ||
            proposal.Status != ProposalStatus.Applied ||
            proposal.SourceType != ProposalSourceType.Queue ||
            !string.Equals(proposal.SourceReferenceId, item.Id.ToString(), StringComparison.OrdinalIgnoreCase) ||
            proposal.RequestedByUserId != item.UserId)
        {
            return (payload, item.BoardId, false);
        }

        var resolvedBoardId = item.BoardId ?? proposal.BoardId;
        var convertedPayload = CaptureRequestContract.WithProvenance(
            payload,
            item.Id,
            proposalId: proposal.Id,
            boardId: resolvedBoardId,
            convertedAt: CaptureConversionTimestamp.ResolveConvertedAt(proposal.AppliedAt));

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
        var payloadResult = CaptureRequestContract.ParsePayload(item.Payload, allowServerAttributionFields: true);
        if (payloadResult.IsSuccess)
            return payloadResult.Value;

        return new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            item.Payload);
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
