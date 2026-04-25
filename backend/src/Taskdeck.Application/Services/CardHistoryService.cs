using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Builds a card history ledger for the proposal review History section.
/// Collects audit log entries and proposal operations for all cards affected
/// by a given proposal, then formats them as numbered history rows.
/// </summary>
public class CardHistoryService : ICardHistoryService
{
    private const int MaxAuditEntriesPerCard = 200;

    private readonly IUnitOfWork _unitOfWork;

    public CardHistoryService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IReadOnlyList<CardHistoryRowDto>>> GetCardHistoryForProposalAsync(
        Guid proposalId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (proposalId == Guid.Empty)
            return Result.Failure<IReadOnlyList<CardHistoryRowDto>>(
                ErrorCodes.ValidationError, "Proposal ID cannot be empty");

        if (userId == Guid.Empty)
            return Result.Failure<IReadOnlyList<CardHistoryRowDto>>(
                ErrorCodes.ValidationError, "User ID cannot be empty");

        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(proposalId, cancellationToken);
        if (proposal == null)
            return Result.Failure<IReadOnlyList<CardHistoryRowDto>>(
                ErrorCodes.NotFound, $"Proposal with ID {proposalId} not found");

        // Extract distinct card IDs from the proposal's operations.
        // Operations target cards when TargetType is "Card" and TargetId is a valid GUID.
        var affectedCardIds = proposal.Operations
            .Where(op => string.Equals(op.TargetType, "Card", StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrWhiteSpace(op.TargetId)
                         && Guid.TryParse(op.TargetId, out _))
            .Select(op => Guid.Parse(op.TargetId!))
            .Distinct()
            .ToList();

        if (affectedCardIds.Count == 0)
        {
            // No card targets found -- return only the current proposal's operations as pending rows.
            var pendingOnlyRows = BuildPendingOperationRows(proposal, DateTimeOffset.UtcNow);
            return Result.Success<IReadOnlyList<CardHistoryRowDto>>(pendingOnlyRows);
        }

        var now = DateTimeOffset.UtcNow;

        // Collect audit log entries for all affected cards (bounded per card).
        var allEntries = new List<HistoryEntry>();

        foreach (var cardId in affectedCardIds)
        {
            var auditLogs = await _unitOfWork.AuditLogs.GetByEntityAsync(
                "Card", cardId, MaxAuditEntriesPerCard, cancellationToken);

            foreach (var log in auditLogs)
            {
                allEntries.Add(new HistoryEntry(
                    log.Timestamp,
                    FormatAuditEvent(log),
                    CardHistoryStatus.Past));
            }
        }

        // Find proposals that targeted the same cards (applied proposals get 'applied' status).
        foreach (var cardId in affectedCardIds)
        {
            var relatedProposal = await _unitOfWork.AutomationProposals
                .GetLatestByOperationTargetAsync("Card", cardId.ToString(), cancellationToken);

            if (relatedProposal != null && relatedProposal.Id != proposalId)
            {
                var status = relatedProposal.Status == ProposalStatus.Applied
                    ? CardHistoryStatus.Applied
                    : CardHistoryStatus.Past;

                allEntries.Add(new HistoryEntry(
                    relatedProposal.UpdatedAt,
                    $"Proposal \"{relatedProposal.Summary}\" {FormatProposalStatus(relatedProposal.Status)}",
                    status));
            }
        }

        // Add current proposal's operations as 'pending' entries.
        foreach (var op in proposal.Operations)
        {
            allEntries.Add(new HistoryEntry(
                proposal.CreatedAt,
                FormatOperationEvent(op),
                CardHistoryStatus.Pending));
        }

        // Sort by timestamp descending (newest first), then assign serial numbers.
        var sorted = allEntries
            .OrderByDescending(e => e.Timestamp)
            .ToList();

        var rows = new List<CardHistoryRowDto>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            var entry = sorted[i];
            rows.Add(new CardHistoryRowDto(
                FormatSerial(i + 1),
                entry.Event,
                FormatAge(entry.Timestamp, now),
                entry.Status));
        }

        return Result.Success<IReadOnlyList<CardHistoryRowDto>>(rows);
    }

    private List<CardHistoryRowDto> BuildPendingOperationRows(
        AutomationProposal proposal, DateTimeOffset now)
    {
        var rows = new List<CardHistoryRowDto>(proposal.Operations.Count);
        for (var i = 0; i < proposal.Operations.Count; i++)
        {
            var op = proposal.Operations[i];
            rows.Add(new CardHistoryRowDto(
                FormatSerial(i + 1),
                FormatOperationEvent(op),
                FormatAge(proposal.CreatedAt, now),
                CardHistoryStatus.Pending));
        }
        return rows;
    }

    /// <summary>
    /// Formats a 1-based index as '#001', '#002', etc.
    /// </summary>
    internal static string FormatSerial(int index)
    {
        return $"#{index:D3}";
    }

    /// <summary>
    /// Formats a timestamp as a relative age string:
    /// - Same day (UTC): just time "11:42"
    /// - Yesterday (UTC): "yest 16:04"
    /// - Same week (within 6 days, UTC): "Mon 11:00"
    /// - Older: "Apr 15"
    /// All formatting uses UTC to avoid timezone ambiguity.
    /// </summary>
    internal static string FormatAge(DateTimeOffset timestamp, DateTimeOffset now)
    {
        var utcTimestamp = timestamp.UtcDateTime;
        var utcNow = now.UtcDateTime;

        // Compare dates in UTC
        var timestampDate = utcTimestamp.Date;
        var nowDate = utcNow.Date;

        if (timestampDate == nowDate)
        {
            // Same day: just time
            return utcTimestamp.ToString("H:mm");
        }

        if (timestampDate == nowDate.AddDays(-1))
        {
            // Yesterday
            return $"yest {utcTimestamp:H:mm}";
        }

        var daysDiff = (nowDate - timestampDate).Days;
        if (daysDiff >= 2 && daysDiff <= 6)
        {
            // This week (2-6 days ago): abbreviated day name + time
            return $"{utcTimestamp:ddd} {utcTimestamp:H:mm}";
        }

        // Older: abbreviated month + day
        return $"{utcTimestamp:MMM} {utcTimestamp:dd}";
    }

    private static string FormatAuditEvent(AuditLog log)
    {
        var entityType = log.EntityType;
        return log.Action switch
        {
            AuditAction.Created => $"{entityType} created",
            AuditAction.Updated => FormatUpdateEvent(entityType, log.Changes),
            AuditAction.Deleted => $"{entityType} deleted",
            AuditAction.Archived => $"{entityType} archived",
            AuditAction.Unarchived => $"{entityType} restored from archive",
            AuditAction.Moved => $"{entityType} moved",
            AuditAction.PermissionGranted => $"{entityType} permission granted",
            AuditAction.PermissionRevoked => $"{entityType} permission revoked",
            AuditAction.OwnershipTransferred => $"{entityType} ownership transferred",
            _ => $"{entityType} {log.Action.ToString().ToLowerInvariant()}"
        };
    }

    private static string FormatUpdateEvent(string entityType, string? changes)
    {
        if (string.IsNullOrWhiteSpace(changes))
            return $"{entityType} updated";

        // Try to extract a more descriptive message from the changes JSON.
        // Common patterns: {"title":"new title"}, {"columnId":"..."}, etc.
        if (changes.Contains("title", StringComparison.OrdinalIgnoreCase))
            return $"{entityType} title updated";
        if (changes.Contains("columnId", StringComparison.OrdinalIgnoreCase)
            || changes.Contains("column", StringComparison.OrdinalIgnoreCase))
            return $"{entityType} moved to new column";
        if (changes.Contains("description", StringComparison.OrdinalIgnoreCase))
            return $"{entityType} description updated";
        if (changes.Contains("position", StringComparison.OrdinalIgnoreCase))
            return $"{entityType} position changed";
        if (changes.Contains("label", StringComparison.OrdinalIgnoreCase))
            return $"{entityType} labels updated";

        return $"{entityType} updated";
    }

    private static string FormatOperationEvent(AutomationProposalOperation op)
    {
        var action = op.ActionType.ToLowerInvariant();
        var target = op.TargetType;

        return action switch
        {
            "create" => $"Create {target}",
            "move" => $"Move {target}",
            "update" => $"Update {target}",
            "archive" => $"Archive {target}",
            "delete" => $"Delete {target}",
            "bulkmove" or "bulk_move" => $"Bulk move {target}",
            "createcolumn" or "create_column" => $"Create column",
            _ => $"{op.ActionType} {target}"
        };
    }

    private static string FormatProposalStatus(ProposalStatus status)
    {
        return status switch
        {
            ProposalStatus.PendingReview => "pending review",
            ProposalStatus.Approved => "approved",
            ProposalStatus.Rejected => "rejected",
            ProposalStatus.Applied => "applied",
            ProposalStatus.Failed => "failed",
            ProposalStatus.Expired => "expired",
            ProposalStatus.Dismissed => "dismissed",
            _ => status.ToString().ToLowerInvariant()
        };
    }

    private sealed record HistoryEntry(DateTimeOffset Timestamp, string Event, CardHistoryStatus Status);
}
