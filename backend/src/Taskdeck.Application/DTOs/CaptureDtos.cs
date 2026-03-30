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
    int RetryCount,
    CaptureProvenanceV1? Provenance = null);

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

public record CaptureTriageEnqueueResultDto(
    Guid Id,
    CaptureStatus Status,
    bool AlreadyTriaging);

public record CaptureTriageProposalResultDto(
    Guid CaptureItemId,
    Guid TriageRunId,
    Guid ProposalId,
    int OperationCount,
    string PromptVersion,
    string Provider,
    string Model);

/// <summary>
/// Describes a single item action within a batch triage request.
/// </summary>
public record BatchTriageItemActionDto(
    Guid ItemId,
    string Action);

/// <summary>
/// Request payload for batch triage operations.
/// Supported actions: "triage", "ignore", "cancel".
/// </summary>
public record BatchTriageRequestDto(
    IReadOnlyList<BatchTriageItemActionDto> Items);

/// <summary>
/// Result for a single item within a batch triage operation.
/// </summary>
public record BatchTriageItemResultDto(
    Guid ItemId,
    bool Success,
    string? ErrorCode = null,
    string? ErrorMessage = null);

/// <summary>
/// Aggregate result of a batch triage operation.
/// </summary>
public record BatchTriageResultDto(
    int Total,
    int Succeeded,
    int Failed,
    IReadOnlyList<BatchTriageItemResultDto> Results);

/// <summary>
/// Request payload for editing the suggestion text of a capture item before triage.
/// </summary>
public record UpdateCaptureSuggestionDto(
    string Text,
    string? TitleHint = null);
