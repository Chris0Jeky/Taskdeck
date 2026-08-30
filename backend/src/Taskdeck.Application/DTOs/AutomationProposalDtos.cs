using System.Text.Json.Serialization;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

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
    /// The revision pinned at approve time that Apply will materialize (#1428). <c>null</c> is NOT
    /// an approval signal: <c>Approve</c> is the only writer, so null covers "approved from the
    /// original operations" AND every proposal Approve never ran on — pending, rejected, expired,
    /// and dismissed-from-rejected alike. Note that Reject DOES set <see cref="DecidedAt"/>, so
    /// "not yet decided" does not describe the null set. A non-null value is the only positive signal.
    /// A set pin also means <see cref="Operations"/> and <see cref="Presentation"/> carry that pinned
    /// revision's effective operation set — on LIST items as well as on single-proposal responses
    /// (get by id, approve, reject) since #1444. The former boundary, where list items exposed the pin
    /// but deliberately mapped the ORIGINAL operations to avoid a per-item revision lookup, is GONE:
    /// a batched two-phase read resolves the whole page, so a client no longer needs the
    /// single-proposal read or the diff endpoint to obtain a listed proposal's effective set, and
    /// doing so would reintroduce the N+1 that boundary existed to avoid.
    /// </summary>
    public Guid? ApprovedRevisionId { get; init; }

    /// <summary>
    /// The effective/latest saved revision for a pending proposal. This is a review concurrency
    /// snapshot, not an approval pin; non-pending proposals expose null and use
    /// <see cref="ApprovedRevisionId"/> for the separate approve-time contract.
    /// </summary>
    public Guid? LatestRevisionId { get; init; }
}

/// <summary>
/// Result of an all-or-none batch approval. Every listed proposal has moved to
/// <see cref="ProposalStatus.Approved"/>; none has been executed or applied.
/// </summary>
public sealed record BatchApproveProposalsResultDto(IReadOnlyList<Guid> ApprovedIds);

/// <summary>The exact pending proposal and revision snapshot selected for batch approval.</summary>
public sealed record BatchApproveProposalSelectionDto(
    Guid Id,
    DateTimeOffset ExpectedProposalUpdatedAt,
    Guid? ExpectedLatestRevisionId);

/// <summary>
/// One reviewer-selected proposal in a per-proposal batch execute request (#1307, q-14 C).
/// <para>
/// <paramref name="ApprovedRevisionId"/> is the caller's echo of the pin the review surface showed
/// (<see cref="ProposalDto.ApprovedRevisionId"/>). It is a REQUIRED member of the wire contract but a
/// NULLABLE value: a proposal approved from its original operations legitimately pins nothing, and
/// <c>null</c> is the only correct echo for it. A mismatch fails that item closed with
/// a <c>Conflict</c> error code so a proposal whose approved content changed under the reviewer
/// is never applied on stale consent.
/// </para>
/// </summary>
public sealed record BatchExecuteProposalSelectionDto(
    Guid ProposalId,
    Guid? ApprovedRevisionId,
    string IdempotencyKey);

/// <summary>
/// Per-proposal outcome of a batch execute. Deliberately three-valued rather than a boolean:
/// <see cref="Applied"/> is claimed ONLY by the call that performed the board write, so a replay of
/// the same idempotency keys reports <see cref="Skipped"/> instead of re-claiming an apply that this
/// call did not do. Both are success outcomes; the whole response is 200 either way.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BatchExecuteOutcome
{
    /// <summary>This call executed the proposal and moved it to Applied.</summary>
    Applied = 0,

    /// <summary>The proposal was already Applied before this call (idempotent replay); nothing was written.</summary>
    Skipped = 1,

    /// <summary>
    /// This item failed; <c>ErrorCode</c> carries the reason. Mostly the same code single execute
    /// would have returned, but NOT always: a proposal the caller cannot see reports
    /// <c>NotFound</c> here where single execute answers <c>Forbidden</c> (so a batch cannot
    /// enumerate other people's proposals), and <c>Conflict</c> from the approved-revision echo has
    /// no single-execute counterpart at all, since that endpoint takes no pin.
    /// </summary>
    Failed = 2,
}

/// <summary>
/// One item's receipt. <c>AppliedOperations</c> is populated for <see cref="BatchExecuteOutcome.Applied"/>
/// only: a skipped replay did not count operations on this call, and a failed item applied none.
/// </summary>
public sealed record BatchExecuteProposalResultDto(
    Guid ProposalId,
    BatchExecuteOutcome Outcome,
    string? ErrorCode,
    string? ErrorMessage,
    int? AppliedOperations);

/// <summary>
/// Receipt for a per-proposal batch execute. Items are returned in request order. There is no
/// whole-batch rollback: each proposal executed in its own transaction through the single-execute
/// code path, so a failure isolates to its own item.
/// </summary>
public sealed record BatchExecuteProposalsResultDto(IReadOnlyList<BatchExecuteProposalResultDto> Results);

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
)
{
    /// <summary>
    /// Trusted application-side confidence metadata. This property is deliberately excluded from
    /// the HTTP contract: callers cannot label their own proposal confidence as model-reported.
    /// Capture triage sets it only after schema-v2 validation or deterministic fallback selection.
    /// </summary>
    [JsonIgnore]
    public TrustedProposalConfidenceInput? TrustedConfidence { get; init; }
}

public record CreateProposalOperationDto(
    int Sequence,
    string ActionType,
    string TargetType,
    string Parameters,
    string IdempotencyKey,
    string? TargetId = null,
    string? ExpectedVersion = null
);

/// <summary>
/// Trusted application-side evidence metadata for schema-v2 transcript proposals. This is not an
/// API request shape: only the transcript triage pipeline supplies it after resolving quote spans.
/// </summary>
public sealed record TranscriptEvidenceLinkInput(
    int OperationSequence,
    Guid TranscriptId,
    int? SpanStart = null,
    int? SpanEnd = null);

/// <summary>Trusted per-operation confidence metadata supplied by an application pipeline.</summary>
public sealed record TrustedProposalConfidenceInput(
    ProvenanceConfidenceSource Source,
    IReadOnlyList<ProposalOperationConfidenceInput> Operations);

/// <summary>Confidence for one proposal operation, keyed by the persisted operation sequence.</summary>
public sealed record ProposalOperationConfidenceInput(
    int OperationSequence,
    double? Value);

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
