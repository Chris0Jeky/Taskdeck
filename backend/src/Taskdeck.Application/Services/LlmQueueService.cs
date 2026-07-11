using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class LlmQueueService : ILlmQueueService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthorizationService _authorizationService;
    private readonly DevelopmentSandboxSettings _sandboxSettings;

    // ProcessNextRequestAsync claims the single oldest claimable non-capture request, so it only
    // needs the oldest N candidates — not the full Pending backlog. Bounds the claim-scan window
    // (#1237). 100 leaves ample headroom for lost optimistic-claim races before falling through to
    // "no pending requests" (the caller retries).
    private const int ClaimScanLimit = 100;

    public LlmQueueService(
        IUnitOfWork unitOfWork,
        IAuthorizationService authorizationService,
        DevelopmentSandboxSettings? sandboxSettings = null)
    {
        _unitOfWork = unitOfWork;
        _authorizationService = authorizationService;
        _sandboxSettings = sandboxSettings ?? new DevelopmentSandboxSettings();
    }

    public async Task<Result<LlmRequestDto>> AddToQueueAsync(Guid userId, CreateLlmRequestDto dto)
    {
        try
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null)
                return Result.Failure<LlmRequestDto>(ErrorCodes.NotFound, $"User with ID {userId} not found");

            if (dto.BoardId.HasValue)
            {
                var permissionResult = await _authorizationService.CanReadBoardAsync(userId, dto.BoardId.Value);
                if (!permissionResult.IsSuccess)
                {
                    return Result.Failure<LlmRequestDto>(permissionResult.ErrorCode, permissionResult.ErrorMessage);
                }

                if (!permissionResult.Value)
                {
                    return Result.Failure<LlmRequestDto>(ErrorCodes.Forbidden, "You do not have access to this board");
                }
            }

            var requestTypeValidation = CaptureRequestContract.ValidateRequestType(dto.RequestType);
            if (!requestTypeValidation.IsSuccess)
            {
                return Result.Failure<LlmRequestDto>(requestTypeValidation.ErrorCode, requestTypeValidation.ErrorMessage);
            }

            var requestType = dto.RequestType;
            var payload = dto.Payload;
            if (CaptureRequestContract.IsCaptureRequestType(requestType))
            {
                var payloadResult = CaptureRequestContract.ParsePayload(payload, allowServerAttributionFields: false);
                if (!payloadResult.IsSuccess)
                {
                    return Result.Failure<LlmRequestDto>(payloadResult.ErrorCode, payloadResult.ErrorMessage);
                }

                // Normalize the type from the parsed payload's source, not the caller's string, so
                // a transcript payload enqueued under the general capture type (or vice versa)
                // still lands in the correct worker lane.
                requestType = CaptureRequestContract.ResolveRequestTypeForSource(payloadResult.Value.Source);
                payload = CaptureRequestContract.SerializePayload(payloadResult.Value);
            }

            var request = new LlmRequest(userId, requestType, payload, dto.BoardId);
            await _unitOfWork.LlmQueue.AddAsync(request);
            await _unitOfWork.SaveChangesAsync();

            return Result.Success(MapToDto(request));
        }
        catch (DomainException ex)
        {
            return Result.Failure<LlmRequestDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<IEnumerable<LlmRequestDto>>> GetUserQueueAsync(Guid userId)
    {
        var requests = await _unitOfWork.LlmQueue.GetByUserAsync(userId);
        return Result.Success(requests.Select(MapToDto));
    }

    public async Task<Result<IEnumerable<LlmRequestDto>>> GetQueueByStatusAsync(Guid userId, RequestStatus status)
    {
        var requests = await _unitOfWork.LlmQueue.GetByUserAndStatusAsync(userId, status);
        return Result.Success(requests.Select(MapToDto));
    }

    public async Task<Result> CancelRequestAsync(Guid requestId, Guid userId)
    {
        try
        {
            var request = await _unitOfWork.LlmQueue.GetByIdAsync(requestId);
            if (request == null)
                return Result.Failure(ErrorCodes.NotFound, $"Request with ID {requestId} not found");

            if (!_sandboxSettings.Enabled && request.UserId != userId)
                return Result.Failure(ErrorCodes.Forbidden, "You do not have permission to cancel this request");

            request.Cancel();
            await _unitOfWork.SaveChangesAsync();

            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<LlmRequestDto>> ProcessNextRequestAsync()
    {
        try
        {
            // Bounded, type-aware, oldest-first at the database (reuses the #1236 primitive) instead
            // of loading the entire Pending backlog and filtering/sorting non-capture in memory (#1237).
            var candidates = await _unitOfWork.LlmQueue.GetOldestPendingNonCaptureAsync(ClaimScanLimit);

            foreach (var candidate in candidates)
            {
                var claimed = await _unitOfWork.LlmQueue.TryClaimProcessingAsync(
                    candidate.Id, candidate.UpdatedAt);
                if (!claimed)
                    continue;

                // TryClaimProcessingAsync refreshes the tracked entity on success, so the
                // candidate now reflects the claimed Processing state from the database.
                return Result.Success(MapToDto(candidate));
            }

            return Result.Failure<LlmRequestDto>(ErrorCodes.NotFound, "No pending requests in the queue");
        }
        catch (DomainException ex)
        {
            return Result.Failure<LlmRequestDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<QueueStatsDto>> GetQueueStatsAsync(Guid userId)
    {
        var statusCounts = await _unitOfWork.LlmQueue.GetStatusCountsByUserAsync(userId);

        var stats = new QueueStatsDto(
            statusCounts.GetValueOrDefault(RequestStatus.Pending, 0),
            statusCounts.GetValueOrDefault(RequestStatus.Processing, 0),
            statusCounts.GetValueOrDefault(RequestStatus.Completed, 0),
            statusCounts.GetValueOrDefault(RequestStatus.Failed, 0));

        return Result.Success(stats);
    }

    private static LlmRequestDto MapToDto(LlmRequest request)
    {
        return new LlmRequestDto(
            request.Id,
            request.UserId,
            request.BoardId,
            request.RequestType,
            request.Status,
            request.ErrorMessage,
            request.CreatedAt,
            request.ProcessedAt,
            request.RetryCount);
    }
}
