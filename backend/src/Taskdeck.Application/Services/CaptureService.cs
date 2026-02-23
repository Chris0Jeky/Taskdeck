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

            var serializedPayload = CaptureRequestContract.SerializePayload(payload);
            var request = new LlmRequest(
                userId,
                CaptureRequestContract.RequestTypeV1,
                serializedPayload,
                dto.BoardId);

            await _unitOfWork.LlmQueue.AddAsync(request, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDetailDto(request));
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
        IEnumerable<LlmRequest> captureItems = items
            .Where(item => CaptureRequestContract.IsCaptureRequestType(item.RequestType))
            .OrderByDescending(item => item.CreatedAt);

        if (filter.BoardId.HasValue)
        {
            captureItems = captureItems.Where(item => item.BoardId == filter.BoardId.Value);
        }

        var summaries = captureItems
            .Select(MapToSummaryDto)
            .Where(summary => !filter.Status.HasValue || summary.Status == filter.Status.Value)
            .Take(limit)
            .ToList();

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

        return Result.Success(MapToDetailDto(item));
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

        var currentStatus = CaptureStatusPolicy.MapFromQueueStatus(item.Status);
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

        return Result.Failure<CaptureSource>(ErrorCodes.ValidationError, $"Invalid capture source '{source}'");
    }

    private static CaptureItemSummaryDto MapToSummaryDto(LlmRequest item)
    {
        var payload = ParsePayload(item);
        var excerpt = BuildExcerpt(payload.Text);
        var status = CaptureStatusPolicy.MapFromQueueStatus(item.Status);

        return new CaptureItemSummaryDto(
            item.Id,
            item.UserId,
            item.BoardId,
            status,
            payload.Source,
            excerpt,
            item.CreatedAt,
            item.ProcessedAt);
    }

    private static CaptureItemDto MapToDetailDto(LlmRequest item)
    {
        var payload = ParsePayload(item);
        var excerpt = BuildExcerpt(payload.Text);
        var status = CaptureStatusPolicy.MapFromQueueStatus(item.Status);

        return new CaptureItemDto(
            item.Id,
            item.UserId,
            item.BoardId,
            status,
            payload.Source,
            payload.Text,
            excerpt,
            item.CreatedAt,
            item.ProcessedAt,
            item.RetryCount);
    }

    private static CapturePayloadV1 ParsePayload(LlmRequest item)
    {
        var payloadResult = CaptureRequestContract.ParsePayload(item.Payload);
        if (payloadResult.IsSuccess)
            return payloadResult.Value;

        return new CapturePayloadV1(
            CaptureRequestContract.CurrentSchemaVersion,
            CaptureSource.Typed,
            item.Payload);
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
