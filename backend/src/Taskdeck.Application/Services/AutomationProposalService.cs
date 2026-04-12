using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class AutomationProposalService : IAutomationProposalService
{
    private const string CaptureTriageActionType = "create";
    private const string CaptureTriageTargetType = "card";

    private static readonly HashSet<string> KnownActionVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "add",
        "apply",
        "archive",
        "assign",
        "attach",
        "block",
        "create",
        "delete",
        "move",
        "remove",
        "rename",
        "reorder",
        "restore",
        "set",
        "unarchive",
        "unblock",
        "update"
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;

    public AutomationProposalService(
        IUnitOfWork unitOfWork,
        INotificationService? notificationService = null)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService ?? NoOpNotificationService.Instance;
    }

    public async Task<Result<ProposalDto>> CreateProposalAsync(CreateProposalDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var proposal = new AutomationProposal(
                dto.SourceType,
                dto.RequestedByUserId,
                dto.Summary,
                dto.RiskLevel,
                dto.CorrelationId,
                dto.BoardId,
                dto.SourceReferenceId,
                dto.ExpiryMinutes);

            await _unitOfWork.AutomationProposals.AddAsync(proposal, cancellationToken);

            // Add operations if provided
            if (dto.Operations != null)
            {
                foreach (var opDto in dto.Operations)
                {
                    var operation = new AutomationProposalOperation(
                        proposal.Id,
                        opDto.Sequence,
                        opDto.ActionType,
                        opDto.TargetType,
                        opDto.Parameters,
                        opDto.IdempotencyKey,
                        opDto.TargetId,
                        opDto.ExpectedVersion);

                    proposal.AddOperation(operation);
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(proposal));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ProposalDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<ProposalDto>> GetProposalByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
        if (proposal == null)
            return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

        return Result.Success(MapToDto(proposal));
    }

    public async Task<Result<IEnumerable<ProposalDto>>> GetProposalsAsync(ProposalFilterDto? filter = null, CancellationToken cancellationToken = default)
    {
        filter ??= new ProposalFilterDto();
        var limit = filter.Limit <= 0 ? 100 : filter.Limit;

        IEnumerable<AutomationProposal> proposals;

        // Apply filters in order of specificity
        if (filter.UserId.HasValue)
        {
            proposals = await _unitOfWork.AutomationProposals.GetByUserIdAsync(filter.UserId.Value, limit, cancellationToken);
        }
        else if (filter.BoardId.HasValue)
        {
            proposals = await _unitOfWork.AutomationProposals.GetByBoardIdAsync(filter.BoardId.Value, limit, cancellationToken);
        }
        else if (filter.Status.HasValue)
        {
            proposals = await _unitOfWork.AutomationProposals.GetByStatusAsync(filter.Status.Value, limit, cancellationToken);
        }
        else if (filter.RiskLevel.HasValue)
        {
            proposals = await _unitOfWork.AutomationProposals.GetByRiskLevelAsync(filter.RiskLevel.Value, limit, cancellationToken);
        }
        else
        {
            // Get all by status Pending if no filters provided
            proposals = await _unitOfWork.AutomationProposals.GetByStatusAsync(ProposalStatus.PendingReview, limit, cancellationToken);
        }

        // Apply remaining filters in-memory when multiple filters are specified.
        if (filter.Status.HasValue)
            proposals = proposals.Where(p => p.Status == filter.Status.Value);

        if (filter.BoardId.HasValue)
            proposals = proposals.Where(p => p.BoardId == filter.BoardId.Value);

        if (filter.UserId.HasValue)
            proposals = proposals.Where(p => p.RequestedByUserId == filter.UserId.Value);

        if (filter.RiskLevel.HasValue)
            proposals = proposals.Where(p => p.RiskLevel == filter.RiskLevel.Value);

        proposals = proposals.Take(limit);

        return Result.Success(proposals.Select(MapToDto));
    }

    public async Task<Result<ProposalDto>> ApproveProposalAsync(Guid id, Guid decidedByUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
            if (proposal == null)
                return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

            proposal.Approve(decidedByUserId);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var notifyResult = await PublishProposalOutcomeNotificationAsync(proposal, "approved", cancellationToken);
            if (!notifyResult.IsSuccess)
                return Result.Failure<ProposalDto>(notifyResult.ErrorCode, notifyResult.ErrorMessage);

            return Result.Success(MapToDto(proposal));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ProposalDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<ProposalDto>> RejectProposalAsync(Guid id, Guid decidedByUserId, UpdateProposalStatusDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
            if (proposal == null)
                return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

            proposal.Reject(decidedByUserId, dto.Reason);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var notifyResult = await PublishProposalOutcomeNotificationAsync(proposal, "rejected", cancellationToken);
            if (!notifyResult.IsSuccess)
                return Result.Failure<ProposalDto>(notifyResult.ErrorCode, notifyResult.ErrorMessage);

            return Result.Success(MapToDto(proposal));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ProposalDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<ProposalDto>> MarkAsAppliedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
            if (proposal == null)
                return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

            proposal.MarkAsApplied();
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var notifyResult = await PublishProposalOutcomeNotificationAsync(proposal, "applied", cancellationToken);
            if (!notifyResult.IsSuccess)
                return Result.Failure<ProposalDto>(notifyResult.ErrorCode, notifyResult.ErrorMessage);

            return Result.Success(MapToDto(proposal));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ProposalDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<ProposalDto>> MarkAsFailedAsync(Guid id, string failureReason, CancellationToken cancellationToken = default)
    {
        try
        {
            var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
            if (proposal == null)
                return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

            proposal.MarkAsFailed(failureReason);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var notifyResult = await PublishProposalOutcomeNotificationAsync(proposal, "failed", cancellationToken);
            if (!notifyResult.IsSuccess)
                return Result.Failure<ProposalDto>(notifyResult.ErrorCode, notifyResult.ErrorMessage);

            return Result.Success(MapToDto(proposal));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ProposalDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<int>> ExpireProposalsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var expiredProposals = await _unitOfWork.AutomationProposals.GetExpiredAsync(cancellationToken);
            int count = 0;

            foreach (var proposal in expiredProposals)
            {
                proposal.Expire();
                count++;
            }

            if (count > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                foreach (var proposal in expiredProposals)
                {
                    var notifyResult = await PublishProposalOutcomeNotificationAsync(
                        proposal,
                        "expired",
                        cancellationToken);
                    if (!notifyResult.IsSuccess)
                        return Result.Failure<int>(notifyResult.ErrorCode, notifyResult.ErrorMessage);
                }
            }

            return Result.Success(count);
        }
        catch (DomainException ex)
        {
            return Result.Failure<int>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<string>> GetProposalDiffAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(id, cancellationToken);
        if (proposal == null)
            return Result.Failure<string>(ErrorCodes.NotFound, $"Proposal with ID {id} not found");

        if (!string.IsNullOrWhiteSpace(proposal.DiffPreview))
            return Result.Success(proposal.DiffPreview);

        if (proposal.Operations.Count == 0)
            return Result.Failure<string>(ErrorCodes.NotFound, "Diff preview not available for this proposal");

        var orderedOperations = proposal.Operations
            .OrderBy(o => o.Sequence)
            .ToList();

        // Batch-load entity names for resolving IDs to human-readable labels
        var columnNames = new Dictionary<Guid, string>();
        var cardTitles = new Dictionary<Guid, string>();

        if (proposal.BoardId.HasValue)
        {
            try
            {
                var columns = await _unitOfWork.Columns.GetByBoardIdAsync(proposal.BoardId.Value, cancellationToken);
                foreach (var column in columns)
                    columnNames[column.Id] = column.Name;

                var cards = await _unitOfWork.Cards.GetByBoardIdAsync(proposal.BoardId.Value, cancellationToken);
                foreach (var card in cards)
                    cardTitles[card.Id] = card.Title;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Non-critical: if lookups fail, fall back to IDs
            }
        }

        var generatedDiff = string.Join(
            Environment.NewLine,
            orderedOperations.Select(o => DescribeOperationReadable(o, columnNames, cardTitles)));

        return Result.Success(generatedDiff);
    }

    public async Task<Result<int>> DismissProposalsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
            return Result.Success(0);

        try
        {
            var proposals = await _unitOfWork.AutomationProposals.GetByIdsAsync(ids, cancellationToken);
            int dismissed = 0;

            foreach (var proposal in proposals)
            {
                if (proposal.CanBeDismissed)
                {
                    proposal.Dismiss();
                    dismissed++;
                }
                // Skip proposals not in a dismissible state
            }

            if (dismissed > 0)
                await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(dismissed);
        }
        catch (DomainException ex)
        {
            return Result.Failure<int>(ex.ErrorCode, ex.Message);
        }
    }

    private static ProposalDto MapToDto(AutomationProposal proposal)
    {
        return new ProposalDto(
            proposal.Id,
            proposal.SourceType,
            proposal.SourceReferenceId,
            proposal.BoardId,
            proposal.RequestedByUserId,
            proposal.Status,
            proposal.RiskLevel,
            proposal.Summary,
            proposal.DiffPreview,
            proposal.ValidationIssues,
            proposal.CreatedAt,
            proposal.UpdatedAt,
            proposal.ExpiresAt,
            proposal.DecidedAt,
            proposal.DecidedByUserId,
            proposal.AppliedAt,
            proposal.FailureReason,
            proposal.CorrelationId,
            proposal.Operations.Select(MapOperationToDto).ToList()
        )
        {
            Presentation = BuildPresentation(proposal),
            IsExpired = proposal.IsExpired
        };
    }

    private static ProposalOperationDto MapOperationToDto(AutomationProposalOperation operation)
    {
        return new ProposalOperationDto(
            operation.Id,
            operation.ProposalId,
            operation.Sequence,
            operation.ActionType,
            operation.TargetType,
            operation.TargetId,
            operation.Parameters,
            operation.IdempotencyKey,
            operation.ExpectedVersion
        );
    }

    private async Task<Result> PublishProposalOutcomeNotificationAsync(
        AutomationProposal proposal,
        string outcome,
        CancellationToken cancellationToken)
    {
        var publishResult = await _notificationService.PublishAsync(
            new CreateNotificationRequestDto(
                proposal.RequestedByUserId,
                NotificationType.ProposalOutcome,
                "Automation proposal updated",
                $"Your proposal '{proposal.Summary}' is now {outcome}.",
                proposal.BoardId,
                SourceEntityType: "proposal",
                SourceEntityId: proposal.Id,
                DeduplicationKey: $"proposal:{proposal.Id}:{proposal.Status}"),
            cancellationToken);

        if (!publishResult.IsSuccess)
            return Result.Failure(publishResult.ErrorCode, publishResult.ErrorMessage);

        return Result.Success();
    }

    private static ProposalPresentationDto BuildPresentation(AutomationProposal proposal)
    {
        var orderedOperations = proposal.Operations
            .OrderBy(operation => operation.Sequence)
            .ToList();

        var affectedEntities = orderedOperations
            .GroupBy(operation => new
            {
                EntityType = HumanizeTargetType(operation.TargetType),
                operation.TargetId
            })
            .Select(group => new ProposalAffectedEntityDto(
                group.Key.EntityType,
                group.Key.TargetId,
                BuildAffectedEntityLabel(
                    group.Key.EntityType,
                    group.Key.TargetId,
                    group.Select(op => ExtractNamedTarget(op.Parameters)).FirstOrDefault(name => name is not null)),
                group.Count()))
            .ToList();

        var operationHeadlines = orderedOperations
            .Select(DescribeOperation)
            .ToList();

        var isCaptureTaskBatch = IsCaptureTaskBatch(proposal.SourceType, orderedOperations);

        return new ProposalPresentationDto(
            BuildPlainSummary(proposal.Summary, isCaptureTaskBatch, orderedOperations, affectedEntities),
            BuildImpactSummary(orderedOperations.Count, affectedEntities, isCaptureTaskBatch),
            BuildRiskCue(proposal.RiskLevel),
            BuildSourceCue(proposal.SourceType),
            operationHeadlines,
            affectedEntities);
    }

    private static string BuildPlainSummary(
        string summary,
        bool isCaptureTaskBatch,
        IReadOnlyList<AutomationProposalOperation> orderedOperations,
        IReadOnlyList<ProposalAffectedEntityDto> affectedEntities)
    {
        if (orderedOperations.Count == 0)
        {
            return summary;
        }

        if (orderedOperations.Count == 1)
        {
            return $"{summary} This would {LowercaseSentenceLead(DescribeOperation(orderedOperations[0]))}";
        }

        if (isCaptureTaskBatch)
        {
            return $"Create {orderedOperations.Count} task card{Pluralize(orderedOperations.Count)} from the captured note.";
        }

        var entitySummary = affectedEntities.Count switch
        {
            0 => "this workspace",
            1 => affectedEntities[0].Label.ToLowerInvariant(),
            _ => string.Join(", ", affectedEntities.Take(2).Select(entity => entity.EntityType.ToLowerInvariant()))
        };

        return $"{summary} This would apply {orderedOperations.Count} planned changes across {entitySummary}.";
    }

    private static string BuildImpactSummary(int operationCount, IReadOnlyList<ProposalAffectedEntityDto> affectedEntities, bool isCaptureTaskBatch)
    {
        if (operationCount == 0)
        {
            return "No concrete board operations were attached to this proposal.";
        }

        if (isCaptureTaskBatch &&
            affectedEntities.Count == 1 &&
            string.Equals(affectedEntities[0].EntityType, "Card", StringComparison.OrdinalIgnoreCase) &&
            affectedEntities[0].ChangeCount == operationCount)
        {
            return $"{operationCount} task card change{Pluralize(operationCount)} ready for approval.";
        }

        if (affectedEntities.Count == 0)
        {
            return $"{operationCount} change{Pluralize(operationCount)} planned.";
        }

        return $"{operationCount} change{Pluralize(operationCount)} touching {affectedEntities.Count} target surface{Pluralize(affectedEntities.Count)}.";
    }

    private static string BuildRiskCue(RiskLevel riskLevel)
    {
        return riskLevel switch
        {
            RiskLevel.Low => "Low risk. Usually safe to review quickly.",
            RiskLevel.Medium => "Medium risk. Check the affected items before approving.",
            RiskLevel.High => "High risk. Review the affected items and execution order carefully.",
            RiskLevel.Critical => "Critical risk. Treat this as a high-trust change and verify every step.",
            _ => "Review the proposed changes before approving."
        };
    }

    private static string BuildSourceCue(ProposalSourceType sourceType)
    {
        return sourceType switch
        {
            ProposalSourceType.Queue => "Created from Inbox capture triage.",
            ProposalSourceType.Chat => "Created from an automation chat session.",
            ProposalSourceType.Manual => "Created manually from an operator-driven proposal flow.",
            _ => "Created from a review-first automation flow."
        };
    }

    private static string DescribeOperation(AutomationProposalOperation operation)
    {
        var verb = HumanizeActionVerb(operation.ActionType);
        var target = HumanizeTargetType(operation.TargetType).ToLowerInvariant();
        var namedTarget = ExtractNamedTarget(operation.Parameters);

        return namedTarget is null
            ? $"{verb} {target}."
            : $"{verb} {target} \"{namedTarget}\".";
    }

    /// <summary>
    /// Produces a human-readable diff line for a single operation, resolving
    /// card IDs to titles and column IDs to names where possible.
    /// </summary>
    private static string DescribeOperationReadable(
        AutomationProposalOperation operation,
        IReadOnlyDictionary<Guid, string> columnNames,
        IReadOnlyDictionary<Guid, string> cardTitles)
    {
        var verb = HumanizeActionVerb(operation.ActionType);
        var targetType = HumanizeTargetType(operation.TargetType).ToLowerInvariant();
        var isCardTarget = operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase);
        var namedTarget = ExtractNamedTarget(operation.Parameters);

        // Try to resolve card title from lookup when not embedded in parameters
        // Only attempt card-specific lookups when the operation targets a card
        if (namedTarget is null && isCardTarget && !string.IsNullOrWhiteSpace(operation.TargetId))
        {
            if (Guid.TryParse(operation.TargetId, out var targetGuid) && cardTitles.TryGetValue(targetGuid, out var title))
                namedTarget = title;
        }

        // Also try to resolve card title from cardId parameter
        if (namedTarget is null && isCardTarget)
        {
            var cardIdFromParams = ExtractGuidParameter(operation.Parameters, "cardId");
            if (cardIdFromParams.HasValue && cardTitles.TryGetValue(cardIdFromParams.Value, out var title))
                namedTarget = title;
        }

        // Build description, falling back to raw TargetId when no name is available
        var description = namedTarget is not null
            ? $"{operation.Sequence}. {verb} {targetType} \"{namedTarget}\""
            : !string.IsNullOrWhiteSpace(operation.TargetId)
                ? $"{operation.Sequence}. {verb} {targetType} {operation.TargetId}"
                : $"{operation.Sequence}. {verb} {targetType}";

        // Append column context for operations that reference a column
        var columnId = ExtractGuidParameter(operation.Parameters, "columnId");
        if (columnId.HasValue)
        {
            var columnDisplay = columnNames.TryGetValue(columnId.Value, out var columnName)
                ? $"\"{columnName}\""
                : columnId.Value.ToString();

            if (verb == "Move")
                description += $" to column {columnDisplay}";
            else if (verb == "Create")
                description += $" in column {columnDisplay}";
        }

        return description;
    }

    /// <summary>
    /// Extracts a GUID value from a JSON parameters string by property name.
    /// Returns null when the property is missing, not a valid GUID, or the JSON is invalid.
    /// </summary>
    private static Guid? ExtractGuidParameter(string parameters, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(parameters))
            return null;

        try
        {
            using var document = JsonDocument.Parse(parameters);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (!document.RootElement.TryGetProperty(propertyName, out var propertyValue))
                return null;

            if (propertyValue.TryGetGuid(out var guidValue))
                return guidValue;
        }
        catch (JsonException)
        {
            // Malformed JSON — fall through to null
        }

        return null;
    }

    private static string? ExtractNamedTarget(string parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(parameters);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var propertyName in new[] { "title", "name", "boardName", "columnName", "labelName" })
            {
                if (document.RootElement.TryGetProperty(propertyName, out var propertyValue) &&
                    propertyValue.ValueKind == JsonValueKind.String)
                {
                    var value = propertyValue.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string HumanizeActionVerb(string actionType)
    {
        var normalized = actionType
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim();

        if (normalized.Length == 0)
        {
            return "Update";
        }

        var tokens = SplitPascalCase(normalized)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var preferredVerb = tokens.FirstOrDefault(token => KnownActionVerbs.Contains(token))
            ?? tokens.LastOrDefault(token => KnownActionVerbs.Contains(token))
            ?? tokens.FirstOrDefault(token => token.All(char.IsLetter))
            ?? tokens.First();
        return char.ToUpperInvariant(preferredVerb[0]) + preferredVerb[1..].ToLowerInvariant();
    }

    private static string HumanizeTargetType(string targetType)
    {
        if (string.IsNullOrWhiteSpace(targetType))
        {
            return "Item";
        }

        var normalized = targetType
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim();

        var humanized = SplitPascalCase(normalized)
            .Replace("  ", " ")
            .Trim();

        return humanized.Length == 0
            ? "Item"
            : char.ToUpperInvariant(humanized[0]) + humanized[1..];
    }

    private static string LowercaseSentenceLead(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            return sentence;
        }

        return char.ToLowerInvariant(sentence[0]) + sentence[1..];
    }

    private static string BuildAffectedEntityLabel(string entityType, string? entityId, string? namedTarget)
    {
        if (!string.IsNullOrWhiteSpace(namedTarget))
        {
            return $"{entityType} \"{namedTarget}\"";
        }

        if (string.IsNullOrWhiteSpace(entityId))
        {
            return entityType;
        }

        return $"{entityType} {entityId}";
    }

    private static string SplitPascalCase(string value)
    {
        var buffer = new System.Text.StringBuilder(value.Length * 2);

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (index > 0 && char.IsUpper(current) && !char.IsWhiteSpace(value[index - 1]))
            {
                buffer.Append(' ');
            }

            buffer.Append(current);
        }

        return buffer.ToString();
    }

    private static bool IsCaptureTaskBatch(ProposalSourceType sourceType, IReadOnlyList<AutomationProposalOperation> orderedOperations)
    {
        if (sourceType != ProposalSourceType.Queue)
        {
            return false;
        }

        if (orderedOperations.Count < 2)
        {
            return false;
        }

        return orderedOperations.All(operation =>
            string.Equals(operation.ActionType, CaptureTriageActionType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(operation.TargetType, CaptureTriageTargetType, StringComparison.OrdinalIgnoreCase));
    }

    private static string Pluralize(int count) => count == 1 ? string.Empty : "s";
}
