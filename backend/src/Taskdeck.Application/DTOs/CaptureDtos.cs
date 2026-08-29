using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.DTOs;

public record CreateCaptureItemDto(
    Guid? BoardId,
    string Text,
    string? Source = null,
    string? TitleHint = null,
    string? ExternalRef = null,
    DateOnly? DueDate = null,
    IReadOnlyList<string>? Labels = null);

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
    string? ErrorMessage = null,
    CaptureProvenanceV1? Provenance = null,
    bool CanEditSuggestion = false,
    CaptureSuggestionMetadataDto? Metadata = null,
    CaptureDispositionV1? Disposition = null);

/// <summary>
/// User-authored capture metadata that remains inert until proposal approval and execution.
/// Label names are resolved against the proposal board during triage; this contract never creates labels.
/// </summary>
public record CaptureSuggestionMetadataDto(
    DateOnly? DueDate = null,
    IReadOnlyList<string>? Labels = null);

public record CaptureItemSummaryDto(
    Guid Id,
    Guid UserId,
    Guid? BoardId,
    CaptureStatus Status,
    CaptureSource Source,
    string TextExcerpt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    string? ErrorMessage = null,
    CaptureDispositionV1? Disposition = null);

public record CaptureListFilterDto(
    CaptureStatus? Status = null,
    Guid? BoardId = null,
    int Limit = 50);

/// <summary>
/// Optional request body for triage enqueue. Supplies a target board when the capture has none
/// yet — Home quick-capture lands board-less, and triage turns a capture into a board proposal, so
/// it requires a target board. Absent body / null board means "use the board already on the item".
/// </summary>
public record EnqueueTriageRequestDto(
    Guid? BoardId = null);

public record CaptureTriageEnqueueResultDto(
    Guid Id,
    CaptureStatus Status,
    bool AlreadyTriaging);

/// <summary>
/// Result of a capture triage run. <see cref="ProposalId"/> is null for the "triaged, nothing to
/// propose" outcome (an LLM run that deliberately found zero action items): the workers mark the
/// item Completed WITHOUT a linked proposal, which the capture status policy renders as the
/// terminal Triaged state — not Failed, because a correct empty verdict is a successful triage.
/// <see cref="DegradedNotice"/> is non-null when the LLM leg was attempted, could not deliver, and
/// the deterministic extractor produced this result instead (#2192). It names the outcome so the
/// fallback is recorded on the capture rather than being silent; the run still succeeded, so the
/// workers complete the item and never fail or retry it on account of the notice.
/// </summary>
public record CaptureTriageProposalResultDto(
    Guid CaptureItemId,
    Guid TriageRunId,
    Guid? ProposalId,
    int OperationCount,
    string PromptVersion,
    string Provider,
    string Model,
    string? DegradedNotice = null);

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
/// Request payload for editing a capture suggestion before triage. Older clients omit
/// <see cref="Metadata"/> and preserve the stored due date and labels. When supplied, the
/// metadata object is a complete replacement: a null due date and empty/null labels clear them.
/// </summary>
public record UpdateCaptureSuggestionDto(
    string Text,
    string? TitleHint = null,
    CaptureSuggestionMetadataDto? Metadata = null);
