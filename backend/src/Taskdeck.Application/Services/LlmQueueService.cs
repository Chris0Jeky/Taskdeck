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

                requestType = CaptureRequestContract.RequestTypeV1;
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
            var pendingRequests = await _unitOfWork.LlmQueue.GetByStatusAsync(RequestStatus.Pending);
            var request = pendingRequests
                .OrderBy(candidate => candidate.CreatedAt)
                .FirstOrDefault(
                candidate => !CaptureRequestContract.IsCaptureRequestType(candidate.RequestType));
            if (request == null)
                return Result.Failure<LlmRequestDto>(ErrorCodes.NotFound, "No pending requests in the queue");

            request.MarkAsProcessing();
            await _unitOfWork.SaveChangesAsync();

            return Result.Success(MapToDto(request));
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
