using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services.Pipeline;

/// <summary>
/// Records audit log entries for executed automation proposal operations.
/// </summary>
public class ExecutionAuditRecorder
{
    private static readonly Dictionary<string, AuditAction> ActionMap = new()
    {
        { "create", AuditAction.Created },
        { "update", AuditAction.Updated },
        { "archive", AuditAction.Archived },
        { "archive-lifecycle", AuditAction.Archived },
        { "restore-lifecycle", AuditAction.Unarchived },
        { "move", AuditAction.Moved },
        { "reorder", AuditAction.Moved }
    };

    /// <summary>
    /// Card lifecycle actions whose board mutation is staged by <c>CardService.SetArchivedAsync</c>.
    /// On the proposal apply lane that service-level receipt is suppressed
    /// (<c>recordLifecycleAudit: false</c>) so this recorder writes the single Archived/Unarchived
    /// entry, carrying the proposal provenance the service row cannot know. The legacy card/archive
    /// wording is reproduced here verbatim so the receipt keeps its existing meaning; direct API
    /// archive/restore calls are unchanged and still get exactly one service-level receipt.
    /// </summary>
    private static readonly Dictionary<string, string> LifecycleAuditSummaries = new(StringComparer.OrdinalIgnoreCase)
    {
        { "archive-lifecycle", "Card archived; original placement retained" },
        { "restore-lifecycle", "Card restored to original column" }
    };

    private readonly IUnitOfWork _unitOfWork;

    public ExecutionAuditRecorder(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <param name="actorUserId">
    /// The authenticated user who applied the proposal. Execution history answers "who changed the
    /// board", and on the apply lane that is the applier - not necessarily the requester, who may be
    /// a different person entirely (#2978). The requester is preserved as provenance text by
    /// <see cref="BuildAuditChanges"/> instead. Null only on internal lanes with no authenticated
    /// caller, where the requester remains the best available actor.
    /// </param>
    public async Task RecordAsync(
        ProposalOperationDto operation,
        ProposalDto proposal,
        CancellationToken cancellationToken,
        Guid? actorUserId = null)
    {
        var auditAction = ActionMap.TryGetValue(operation.ActionType.ToLowerInvariant(), out var mapped)
            ? mapped
            : AuditAction.Updated;

        var (entityType, entityId) = ResolveAuditEntity(operation, proposal);
        var changes = BuildAuditChanges(operation, proposal);

        var auditLog = new AuditLog(
            entityType,
            entityId,
            auditAction,
            ResolveActor(actorUserId, proposal),
            changes
        );

        await _unitOfWork.AuditLogs.AddAsync(auditLog, cancellationToken);
    }

    /// <summary>
    /// The actor stamped on an execution-history row: the authenticated applying user when the
    /// apply lane knows one, otherwise the proposal's requester. <see cref="AuditLog"/> refuses an
    /// empty user id, so an empty actor is treated as "no authenticated caller" rather than being
    /// allowed to throw inside the execution transaction.
    /// </summary>
    public static Guid ResolveActor(Guid? actorUserId, ProposalDto proposal) =>
        actorUserId is Guid actor && actor != Guid.Empty ? actor : proposal.RequestedByUserId;

    public static (string EntityType, Guid EntityId) ResolveAuditEntity(ProposalOperationDto operation, ProposalDto proposal)
    {
        if (!string.IsNullOrWhiteSpace(operation.TargetId) && Guid.TryParse(operation.TargetId, out var targetId))
            return (operation.TargetType, targetId);

        if (OperationParameterParser.TryDeserializeParameters(operation.Parameters, out var parameters, out _))
        {
            if (OperationParameterParser.TryGetGuidFromParameters(parameters, "cardId", out var cardId))
                return ("card", cardId);

            if (OperationParameterParser.TryGetGuidFromParameters(parameters, "columnId", out var columnId))
                return ("column", columnId);

            if (OperationParameterParser.TryGetGuidFromParameters(parameters, "boardId", out var boardId))
                return ("board", boardId);
        }

        if (proposal.BoardId.HasValue)
            return ("board", proposal.BoardId.Value);

        return ("automation-proposal", proposal.Id);
    }

    public static string BuildAuditChanges(ProposalOperationDto operation, ProposalDto proposal)
    {
        var parameterPreview = operation.Parameters.Length <= 500
            ? operation.Parameters
            : operation.Parameters[..500] + "...";

        // The row's UserId is the applying user (#2978), so the requester has to survive somewhere:
        // it is named here, keeping "who asked for this" readable next to "who applied it" even when
        // they are two different people.
        var provenance = $"Automation proposal {proposal.Id} requested by user {proposal.RequestedByUserId}, sequence {operation.Sequence}: {operation.ActionType} {operation.TargetType}. Parameters: {parameterPreview}";

        // Card lifecycle receipts keep the legacy CardService wording ahead of the proposal
        // provenance, so the single entry reads the same as a direct archive/restore receipt
        // while still naming the proposal that authorised it.
        return LifecycleAuditSummaries.TryGetValue(operation.ActionType, out var lifecycleSummary)
            ? $"{lifecycleSummary}. {provenance}"
            : provenance;
    }
}
