using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.DTOs;

// LLM Queue DTOs
public record LlmRequestDto(
    Guid Id,
    Guid UserId,
    Guid? BoardId,
    string RequestType,
    RequestStatus Status,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    int RetryCount);

public record CreateLlmRequestDto(
    string RequestType,
    string Payload,
    Guid? BoardId = null);

public record QueueStatsDto(
    int PendingCount,
    int ProcessingCount,
    int CompletedCount,
    int FailedCount);
