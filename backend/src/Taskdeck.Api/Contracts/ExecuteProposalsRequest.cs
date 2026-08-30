using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Taskdeck.Api.Contracts;

/// <summary>
/// Explicit proposal selection for the per-proposal batch-execute endpoint (#1307, q-14 C).
/// Unlike batch approve, this request is NOT all-or-none: each listed proposal executes
/// independently and reports its own outcome.
/// </summary>
public sealed class ExecuteProposalsRequest
{
    [Required]
    public List<ExecuteProposalSelectionRequest>? Proposals { get; init; }
}

/// <summary>
/// One proposal to execute, with the reviewer's echo of its approved-revision pin and the
/// idempotency key for this item.
/// </summary>
public sealed class ExecuteProposalSelectionRequest
{
    private readonly Guid? _approvedRevisionId;

    public Guid ProposalId { get; init; }

    /// <summary>
    /// The reviewer's echo of <c>ProposalDto.ApprovedRevisionId</c>. REQUIRED as a wire member,
    /// NULLABLE as a value: a proposal approved from its original operations pins nothing, and
    /// <c>null</c> is the only correct echo for it. Omitting the key entirely is a 400 — see
    /// <see cref="HasApprovedRevisionId"/> — so a client cannot silently skip the drift check by
    /// leaving the field out of its payload.
    /// </summary>
    public Guid? ApprovedRevisionId
    {
        get => _approvedRevisionId;
        init
        {
            _approvedRevisionId = value;
            HasApprovedRevisionId = true;
        }
    }

    /// <summary>
    /// True when the payload actually carried an <c>approvedRevisionId</c> key. System.Text.Json
    /// invokes the init accessor only for properties present in the JSON, so this distinguishes an
    /// explicit <c>null</c> (a valid echo) from an omitted field (a malformed request).
    /// </summary>
    [JsonIgnore]
    public bool HasApprovedRevisionId { get; private init; }

    public string? IdempotencyKey { get; init; }
}
