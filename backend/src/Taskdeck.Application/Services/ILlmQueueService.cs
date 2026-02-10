using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

/// <summary>
/// Service interface for LLM request queue operations.
/// SCAFFOLDING: Implementation pending.
/// </summary>
public interface ILlmQueueService
{
    Task<Result<LlmRequestDto>> AddToQueueAsync(CreateLlmRequestDto dto);
    Task<Result<IEnumerable<LlmRequestDto>>> GetUserQueueAsync(Guid userId);
    Task<Result<IEnumerable<LlmRequestDto>>> GetQueueByStatusAsync(RequestStatus status);
    Task<Result> CancelRequestAsync(Guid requestId, Guid userId);
    Task<Result<LlmRequestDto>> ProcessNextRequestAsync();
    Task<Result<QueueStatsDto>> GetQueueStatsAsync();
}
