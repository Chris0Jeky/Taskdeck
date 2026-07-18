using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.DTOs;

public record ProposalDto(
    Guid Id,
    ProposalSourceType SourceType,
    string? SourceReferenceId,
    Guid? BoardId,
    Guid RequestedByUserId,
    ProposalStatus Status,
    RiskLevel RiskLevel,
    string Summary,
    string? DiffPreview,
    string? ValidationIssues,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTime ExpiresAt,
    DateTime? DecidedAt,
    Guid? DecidedByUserId,
    DateTime? AppliedAt,
    string? FailureReason,
    string CorrelationId,
    List<ProposalOperationDto> Operations
)
{
    public ProposalPresentationDto Presentation { get; init; } = ProposalPresentationDto.Empty;

    /// <summary>
    /// True when the proposal's expiry time has passed, regardless of the current status.
    /// This allows the frontend to distinguish approved-but-expired proposals from executable ones.
    /// </summary>
    public bool IsExpired { get; init; }

    /// <summary>
    /// When set and in the future, the proposal is snoozed until this UTC instant. Exposed so the
    /// client clock can resurface it in-session and a re-snooze toast can confirm the new window.
    /// </summary>
    public DateTime? DeferredUntil { get; init; }

    /// <summary>
    /// The revision pinned at approve time that Apply will materialize (#1428), or <c>null</c>
    /// when the proposal was approved from its original operations (or is not yet decided). When
    /// set, <see cref="Operations"/> reflects that pinned revision's effective operation set.
    /// </summary>
    public Guid? ApprovedRevisionId { get; init; }
}

public record ProposalPresentationDto(
    string PlainSummary,
    string ImpactSummary,
    string RiskCue,
    string SourceCue,
    IReadOnlyList<string> OperationHeadlines,
    IReadOnlyList<ProposalAffectedEntityDto> AffectedEntities)
{
    public static ProposalPresentationDto Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        Array.Empty<string>(),
        Array.Empty<ProposalAffectedEntityDto>());
}

public record ProposalAffectedEntityDto(
    string EntityType,
    string? EntityId,
    string Label,
    int ChangeCount);

public record ProposalOperationDto(
    Guid Id,
    Guid ProposalId,
    int Sequence,
    string ActionType,
    string TargetType,
    string? TargetId,
    string Parameters,
    string IdempotencyKey,
    string? ExpectedVersion
);

public record CreateProposalDto(
    ProposalSourceType SourceType,
    Guid RequestedByUserId,
    string Summary,
    RiskLevel RiskLevel,
    string CorrelationId,
    Guid? BoardId = null,
    string? SourceReferenceId = null,
    int ExpiryMinutes = 1440,
    List<CreateProposalOperationDto>? Operations = null,
    string? ProvenanceModelId = null,
    int ProvenanceTotalTokens = 0
);

public record CreateProposalOperationDto(
    int Sequence,
    string ActionType,
    string TargetType,
    string Parameters,
    string IdempotencyKey,
    string? TargetId = null,
    string? ExpectedVersion = null
);

public record UpdateProposalStatusDto(
    string? Reason = null
);

/// <summary>
/// Request body for the snooze (defer) endpoint. A null DurationMinutes applies the
/// default window; an override is clamped to [1, 1440] at the API boundary.
/// </summary>
public record DeferProposalRequestDto(int? DurationMinutes = null);

/// <summary>
/// Request body for the report-bad-suggestion endpoint. Reason is an OPTIONAL
/// ProposalFeedbackReason enum NAME (never free text); null/empty maps to Unspecified.
/// </summary>
public record ReportProposalFeedbackDto(string? Reason = null);

public record ProposalFilterDto(
    ProposalStatus? Status = null,
    Guid? BoardId = null,
    Guid? UserId = null,
    RiskLevel? RiskLevel = null,
    int Limit = 100
);
