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
    private readonly DevelopmentSandboxSettings _sandboxSettings;

    public LlmQueueService(IUnitOfWork unitOfWork, DevelopmentSandboxSettings? sandboxSettings = null)
    {
        _unitOfWork = unitOfWork;
        _sandboxSettings = sandboxSettings ?? new DevelopmentSandboxSettings();
    }

    public async Task<Result<LlmRequestDto>> AddToQueueAsync(CreateLlmRequestDto dto)
    {
        try
        {
            var user = await _unitOfWork.Users.GetByIdAsync(dto.UserId);
            if (user == null)
                return Result.Failure<LlmRequestDto>(ErrorCodes.NotFound, $"User with ID {dto.UserId} not found");

            if (dto.BoardId.HasValue)
            {
                var board = await _unitOfWork.Boards.GetByIdAsync(dto.BoardId.Value);
                if (board == null)
                    return Result.Failure<LlmRequestDto>(ErrorCodes.NotFound, $"Board with ID {dto.BoardId.Value} not found");
            }

            var request = new LlmRequest(dto.UserId, dto.RequestType, dto.Payload, dto.BoardId);
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

    public async Task<Result<IEnumerable<LlmRequestDto>>> GetQueueByStatusAsync(RequestStatus status)
    {
        var requests = await _unitOfWork.LlmQueue.GetByStatusAsync(status);
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
            var request = await _unitOfWork.LlmQueue.GetNextPendingAsync();
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

    public async Task<Result<QueueStatsDto>> GetQueueStatsAsync()
    {
        var pending = await _unitOfWork.LlmQueue.GetByStatusAsync(RequestStatus.Pending);
        var processing = await _unitOfWork.LlmQueue.GetByStatusAsync(RequestStatus.Processing);
        var completed = await _unitOfWork.LlmQueue.GetByStatusAsync(RequestStatus.Completed);
        var failed = await _unitOfWork.LlmQueue.GetByStatusAsync(RequestStatus.Failed);

        var stats = new QueueStatsDto(
            pending.Count(),
            processing.Count(),
            completed.Count(),
            failed.Count());

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
