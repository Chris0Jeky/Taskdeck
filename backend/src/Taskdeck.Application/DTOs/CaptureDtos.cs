using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.DTOs;

public record CreateCaptureItemDto(
    Guid? BoardId,
    string Text,
    string? Source = null,
    string? TitleHint = null,
    string? ExternalRef = null);

public record CaptureItemDto(
    Guid Id,
    Guid UserId,
    Guid? BoardId,
    CaptureStatus Status,
    CaptureSource Source,
    string RawText,
    string TextExcerpt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    int RetryCount);

public record CaptureItemSummaryDto(
    Guid Id,
    Guid UserId,
    Guid? BoardId,
    CaptureStatus Status,
    CaptureSource Source,
    string TextExcerpt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt);

public record CaptureListFilterDto(
    CaptureStatus? Status = null,
    Guid? BoardId = null,
    int Limit = 50);
