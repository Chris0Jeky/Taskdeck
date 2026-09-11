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

    public async Task RecordAsync(ProposalOperationDto operation, ProposalDto proposal, CancellationToken cancellationToken)
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
            proposal.RequestedByUserId,
            changes
        );

        await _unitOfWork.AuditLogs.AddAsync(auditLog, cancellationToken);
    }

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

        var provenance = $"Automation proposal {proposal.Id}, sequence {operation.Sequence}: {operation.ActionType} {operation.TargetType}. Parameters: {parameterPreview}";

        // Card lifecycle receipts keep the legacy CardService wording ahead of the proposal
        // provenance, so the single entry reads the same as a direct archive/restore receipt
        // while still naming the proposal that authorised it.
        return LifecycleAuditSummaries.TryGetValue(operation.ActionType, out var lifecycleSummary)
            ? $"{lifecycleSummary}. {provenance}"
            : provenance;
    }
}
