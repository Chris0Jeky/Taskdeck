using System.Text.Json;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class AutomationExecutorService : IAutomationExecutorService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAutomationProposalService _proposalService;
    private readonly IAutomationPolicyEngine _policyEngine;
    private readonly CardService _cardService;
    private readonly BoardService _boardService;
    private readonly ColumnService _columnService;
    private readonly ILogger<AutomationExecutorService>? _logger;

    public AutomationExecutorService(
        IUnitOfWork unitOfWork,
        IAutomationProposalService proposalService,
        IAutomationPolicyEngine policyEngine,
        CardService cardService,
        BoardService boardService,
        ColumnService columnService)
        : this(unitOfWork, proposalService, policyEngine, cardService, boardService, columnService, logger: null)
    {
    }

    public AutomationExecutorService(
        IUnitOfWork unitOfWork,
        IAutomationProposalService proposalService,
        IAutomationPolicyEngine policyEngine,
        CardService cardService,
        BoardService boardService,
        ColumnService columnService,
        ILogger<AutomationExecutorService>? logger)
    {
        _unitOfWork = unitOfWork;
        _proposalService = proposalService;
        _policyEngine = policyEngine;
        _cardService = cardService;
        _boardService = boardService;
        _columnService = columnService;
        _logger = logger;
    }

    public async Task<Result> ExecuteProposalAsync(Guid proposalId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;

        if (proposalId == Guid.Empty)
        {
            _logger?.LogWarning("Automation proposal execution rejected: empty proposalId");
            return Result.Failure(ErrorCodes.ValidationError, "ProposalId cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _logger?.LogWarning("Automation proposal execution rejected for proposal {ProposalId}: missing idempotency key", proposalId);
            return Result.Failure(ErrorCodes.ValidationError, "IdempotencyKey cannot be empty");
        }

        // Get proposal
        var proposalResult = await _proposalService.GetProposalByIdAsync(proposalId, cancellationToken);
        if (!proposalResult.IsSuccess)
        {
            _logger?.LogWarning(
                "Automation proposal execution failed for proposal {ProposalId} after {DurationMs}ms: {ErrorCode} {ErrorMessage}",
                proposalId,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                proposalResult.ErrorCode,
                proposalResult.ErrorMessage);
            return Result.Failure(proposalResult.ErrorCode, proposalResult.ErrorMessage);
        }

        var proposal = proposalResult.Value;

        // Idempotent behavior across requests/processes: already-applied proposals are treated as success.
        if (proposal.Status == ProposalStatus.Applied)
        {
            var syncResult = await SyncLinkedCaptureConversionAsync(proposal, cancellationToken);
            if (!syncResult.IsSuccess)
            {
                _logger?.LogWarning(
                    "Already-applied proposal {ProposalId} could not sync linked capture conversion: {ErrorCode} {ErrorMessage}",
                    proposalId,
                    syncResult.ErrorCode,
                    syncResult.ErrorMessage);
            }

            _logger?.LogInformation(
                "Automation proposal execution skipped for already-applied proposal {ProposalId} after {DurationMs}ms",
                proposalId,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            return Result.Success();
        }

        // Verify proposal is approved
        if (proposal.Status != ProposalStatus.Approved)
        {
            _logger?.LogWarning(
                "Automation proposal execution rejected for proposal {ProposalId} after {DurationMs}ms due to status {Status}",
                proposalId,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                proposal.Status);
            return Result.Failure(ErrorCodes.InvalidOperation, $"Cannot execute proposal in status {proposal.Status}");
        }

        // Revalidate policy before execution
        var policyResult = _policyEngine.ValidatePolicy(proposal);
        if (!policyResult.IsSuccess)
        {
            _logger?.LogWarning(
                "Automation proposal execution policy validation failed for proposal {ProposalId} after {DurationMs}ms: {ErrorCode} {ErrorMessage}",
                proposalId,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                policyResult.ErrorCode,
                policyResult.ErrorMessage);
            return Result.Failure(policyResult.ErrorCode, policyResult.ErrorMessage);
        }

        // Revalidate permissions
        var permissionResult = await _policyEngine.ValidatePermissionsAsync(
            proposal.RequestedByUserId, 
            proposal.BoardId, 
            proposal.Operations, 
            cancellationToken);
        if (!permissionResult.IsSuccess)
        {
            _logger?.LogWarning(
                "Automation proposal execution permission validation failed for proposal {ProposalId} after {DurationMs}ms: {ErrorCode} {ErrorMessage}",
                proposalId,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                permissionResult.ErrorCode,
                permissionResult.ErrorMessage);
            return Result.Failure(permissionResult.ErrorCode, permissionResult.ErrorMessage);
        }

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            // Execute operations in sequence order
            var orderedOperations = proposal.Operations.OrderBy(o => o.Sequence).ToList();
            var failedOperation = -1;
            var failedResult = Result.Success();
            var failureReason = "";

            foreach (var operation in orderedOperations)
            {
                var executionResult = await ExecuteOperationAsync(operation, cancellationToken);
                if (!executionResult.IsSuccess)
                {
                    failedOperation = operation.Sequence;
                    failedResult = executionResult;
                    failureReason = $"Operation {operation.Sequence} ({operation.ActionType} {operation.TargetType}) failed: {executionResult.ErrorMessage}";
                    break;
                }

                // Create audit log for the operation
                await CreateAuditLogAsync(operation, proposal, cancellationToken);
            }

            if (failedOperation >= 0)
            {
                // Mark proposal as failed and rollback transaction
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                
                // Update proposal status
                var updateResult = await UpdateProposalStatusAsync(proposalId, ProposalStatus.Failed, failureReason, cancellationToken);
                if (!updateResult.IsSuccess)
                    return Result.Failure(updateResult.ErrorCode, updateResult.ErrorMessage);

                _logger?.LogWarning(
                    "Automation proposal execution failed for proposal {ProposalId} at operation {OperationSequence} after {DurationMs}ms: {FailureReason}",
                    proposalId,
                    failedOperation,
                    (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                    failureReason);
                return Result.Failure(failedResult.ErrorCode, failureReason);
            }

            // Mark proposal as applied
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var markResult = await UpdateProposalStatusAsync(proposalId, ProposalStatus.Applied, null, cancellationToken);
            if (!markResult.IsSuccess)
                return Result.Failure(markResult.ErrorCode, markResult.ErrorMessage);

            var captureSyncResult = await SyncLinkedCaptureConversionAsync(
                proposal with
                {
                    Status = ProposalStatus.Applied,
                    AppliedAt = DateTime.UtcNow
                },
                cancellationToken);
            if (!captureSyncResult.IsSuccess)
            {
                _logger?.LogWarning(
                    "Applied proposal {ProposalId} could not sync linked capture conversion: {ErrorCode} {ErrorMessage}",
                    proposalId,
                    captureSyncResult.ErrorCode,
                    captureSyncResult.ErrorMessage);
            }

            _logger?.LogInformation(
                "Automation proposal execution completed for proposal {ProposalId} in {DurationMs}ms with {OperationCount} operation(s)",
                proposalId,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
                orderedOperations.Count);
            return Result.Success();
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            await UpdateProposalStatusAsync(proposalId, ProposalStatus.Failed, ex.Message, cancellationToken);
            _logger?.LogError(
                ex,
                "Automation proposal execution threw for proposal {ProposalId} after {DurationMs}ms",
                proposalId,
                (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            return Result.Failure(ErrorCodes.UnexpectedError, $"Failed to execute proposal: {ex.Message}");
        }
    }

    private async Task<Result> ExecuteOperationAsync(ProposalOperationDto operation, CancellationToken cancellationToken)
    {
        var actionType = operation.ActionType.ToLowerInvariant();
        var targetType = operation.TargetType.ToLowerInvariant();

        try
        {
            if (targetType == "card")
            {
                return await ExecuteCardOperationAsync(actionType, operation, cancellationToken);
            }
            else if (targetType == "board")
            {
                return await ExecuteBoardOperationAsync(actionType, operation, cancellationToken);
            }
            else if (targetType == "column")
            {
                return await ExecuteColumnOperationAsync(actionType, operation, cancellationToken);
            }
            else
            {
                return Result.Failure(ErrorCodes.ValidationError, $"Unsupported target type: {targetType}");
            }
        }
        catch (Exception ex)
        {
            return Result.Failure(ErrorCodes.UnexpectedError, $"Operation execution failed: {ex.Message}");
        }
    }

    private async Task<Result> ExecuteCardOperationAsync(string actionType, ProposalOperationDto operation, CancellationToken cancellationToken)
    {
        if (!TryDeserializeParameters(operation.Parameters, out var parameters, out var parseError))
            return Result.Failure(ErrorCodes.ValidationError, parseError);

        switch (actionType)
        {
            case "create":
                return await CreateCardAsync(parameters, operation.TargetId, cancellationToken);
            
            case "update":
                return await UpdateCardAsync(parameters, cancellationToken);
            
            case "move":
                return await MoveCardAsync(parameters, cancellationToken);
            
            case "archive":
                return await ArchiveCardAsync(parameters, cancellationToken);
            
            default:
                return Result.Failure(ErrorCodes.ValidationError, $"Unsupported card action: {actionType}");
        }
    }

    private async Task<Result> CreateCardAsync(
        JsonElement parameters,
        string? targetId,
        CancellationToken cancellationToken)
    {
        if (!TryGetRequiredString(parameters, "title", out var title, out var titleError))
            return Result.Failure(ErrorCodes.ValidationError, titleError);

        var description = GetOptionalString(parameters, "description");

        if (!TryGetRequiredGuid(parameters, "columnId", out var columnId, out var columnIdError))
            return Result.Failure(ErrorCodes.ValidationError, columnIdError);

        if (!TryGetRequiredGuid(parameters, "boardId", out var boardId, out var boardIdError))
            return Result.Failure(ErrorCodes.ValidationError, boardIdError);

        Guid? cardId = null;
        if (!string.IsNullOrWhiteSpace(targetId))
        {
            if (!Guid.TryParse(targetId, out var parsedTargetId))
                return Result.Failure(ErrorCodes.ValidationError, "Invalid targetId");

            cardId = parsedTargetId;
        }

        var dto = new CreateCardDto(boardId, columnId, title, description, null, null);
        var result = await _cardService.CreateCardAsync(dto, cardId, cancellationToken);
        
        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result> UpdateCardAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryGetRequiredGuid(parameters, "cardId", out var cardId, out var cardIdError))
            return Result.Failure(ErrorCodes.ValidationError, cardIdError);

        var title = GetOptionalString(parameters, "title");
        var description = GetOptionalString(parameters, "description");
        if (title == null && description == null)
            return Result.Failure(ErrorCodes.ValidationError, "Update card operation requires at least one of 'title' or 'description'");

        var dto = new UpdateCardDto(title, description, null, null, null, null);
        var result = await _cardService.UpdateCardAsync(cardId, dto, cancellationToken);
        
        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result> MoveCardAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryGetRequiredGuid(parameters, "cardId", out var cardId, out var cardIdError))
            return Result.Failure(ErrorCodes.ValidationError, cardIdError);

        if (!TryGetRequiredGuid(parameters, "columnId", out var columnId, out var columnIdError))
            return Result.Failure(ErrorCodes.ValidationError, columnIdError);

        // Get current cards in target column to determine position
        var targetColumn = await _unitOfWork.Columns.GetByIdWithCardsAsync(columnId, cancellationToken);
        if (targetColumn == null)
            return Result.Failure(ErrorCodes.NotFound, $"Column {columnId} not found");
        
        var position = targetColumn.Cards.Any() ? targetColumn.Cards.Max(c => c.Position) + 1 : 0;
        var dto = new MoveCardDto(columnId, position);
        var result = await _cardService.MoveCardAsync(cardId, dto, cancellationToken);
        
        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result> ArchiveCardAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryGetRequiredGuid(parameters, "cardId", out var cardId, out var cardIdError))
            return Result.Failure(ErrorCodes.ValidationError, cardIdError);

        var dto = new UpdateCardDto(null, null, null, true, null, null);
        var result = await _cardService.UpdateCardAsync(cardId, dto, cancellationToken);
        
        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result> ExecuteBoardOperationAsync(string actionType, ProposalOperationDto operation, CancellationToken cancellationToken)
    {
        if (!TryDeserializeParameters(operation.Parameters, out var parameters, out var parseError))
            return Result.Failure(ErrorCodes.ValidationError, parseError);

        switch (actionType)
        {
            case "update":
                return await UpdateBoardAsync(parameters, cancellationToken);
            
            default:
                return Result.Failure(ErrorCodes.ValidationError, $"Unsupported board action: {actionType}");
        }
    }

    private async Task<Result> UpdateBoardAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryGetRequiredGuid(parameters, "boardId", out var boardId, out var boardIdError))
            return Result.Failure(ErrorCodes.ValidationError, boardIdError);

        var name = GetOptionalString(parameters, "name");
        var description = GetOptionalString(parameters, "description");
        var isArchived = GetOptionalBoolean(parameters, "isArchived");
        if (name == null && description == null && !isArchived.HasValue)
            return Result.Failure(ErrorCodes.ValidationError, "Update board operation requires at least one of 'name', 'description', or 'isArchived'");

        var dto = new UpdateBoardDto(name, description, isArchived);
        var result = await _boardService.UpdateBoardAsync(boardId, dto, cancellationToken);
        
        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result> ExecuteColumnOperationAsync(string actionType, ProposalOperationDto operation, CancellationToken cancellationToken)
    {
        if (!TryDeserializeParameters(operation.Parameters, out var parameters, out var parseError))
            return Result.Failure(ErrorCodes.ValidationError, parseError);

        switch (actionType)
        {
            case "reorder":
                return await ReorderColumnAsync(parameters, cancellationToken);
            
            default:
                return Result.Failure(ErrorCodes.ValidationError, $"Unsupported column action: {actionType}");
        }
    }

    private async Task<Result> ReorderColumnAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!TryGetRequiredGuid(parameters, "columnId", out var columnId, out var columnIdError))
            return Result.Failure(ErrorCodes.ValidationError, columnIdError);

        if (!TryGetRequiredInt32(parameters, "position", out var newPosition, out var positionError))
            return Result.Failure(ErrorCodes.ValidationError, positionError);

        if (newPosition < 0)
            return Result.Failure(ErrorCodes.ValidationError, "Invalid position: must be non-negative");

        var dto = new UpdateColumnDto(null, newPosition, null);
        var result = await _columnService.UpdateColumnAsync(columnId, dto, cancellationToken);
        
        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task CreateAuditLogAsync(ProposalOperationDto operation, ProposalDto proposal, CancellationToken cancellationToken)
    {
        var actionMap = new Dictionary<string, AuditAction>
        {
            { "create", AuditAction.Created },
            { "update", AuditAction.Updated },
            { "archive", AuditAction.Archived },
            { "move", AuditAction.Moved },
            { "reorder", AuditAction.Moved }
        };

        var auditAction = actionMap.ContainsKey(operation.ActionType.ToLowerInvariant()) 
            ? actionMap[operation.ActionType.ToLowerInvariant()] 
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

    private static (string EntityType, Guid EntityId) ResolveAuditEntity(ProposalOperationDto operation, ProposalDto proposal)
    {
        if (!string.IsNullOrWhiteSpace(operation.TargetId) && Guid.TryParse(operation.TargetId, out var targetId))
            return (operation.TargetType, targetId);

        if (TryDeserializeParameters(operation.Parameters, out var parameters, out _))
        {
            if (TryGetGuidFromParameters(parameters, "cardId", out var cardId))
                return ("card", cardId);

            if (TryGetGuidFromParameters(parameters, "columnId", out var columnId))
                return ("column", columnId);

            if (TryGetGuidFromParameters(parameters, "boardId", out var boardId))
                return ("board", boardId);
        }

        if (proposal.BoardId.HasValue)
            return ("board", proposal.BoardId.Value);

        return ("automation-proposal", proposal.Id);
    }

    private static string BuildAuditChanges(ProposalOperationDto operation, ProposalDto proposal)
    {
        var parameterPreview = operation.Parameters.Length <= 500
            ? operation.Parameters
            : operation.Parameters[..500] + "...";

        return $"Automation proposal {proposal.Id}, sequence {operation.Sequence}: {operation.ActionType} {operation.TargetType}. Parameters: {parameterPreview}";
    }

    private static bool TryDeserializeParameters(string rawParameters, out JsonElement parameters, out string error)
    {
        parameters = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(rawParameters))
        {
            error = "Operation parameters cannot be empty";
            return false;
        }

        try
        {
            parameters = JsonSerializer.Deserialize<JsonElement>(rawParameters);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid operation parameters JSON: {ex.Message}";
            return false;
        }
    }

    private static bool TryGetRequiredString(JsonElement parameters, string parameterName, out string value, out string error)
    {
        value = string.Empty;
        error = string.Empty;

        if (!parameters.TryGetProperty(parameterName, out var property))
        {
            error = $"Missing required parameter '{parameterName}'";
            return false;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error = $"Parameter '{parameterName}' must be a string";
            return false;
        }

        var parsed = property.GetString();
        if (string.IsNullOrWhiteSpace(parsed))
        {
            error = $"Parameter '{parameterName}' cannot be empty";
            return false;
        }

        value = parsed;
        return true;
    }

    private static string? GetOptionalString(JsonElement parameters, string parameterName)
    {
        if (!parameters.TryGetProperty(parameterName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Null)
            return null;

        if (property.ValueKind != JsonValueKind.String)
            return null;

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool? GetOptionalBoolean(JsonElement parameters, string parameterName)
    {
        if (!parameters.TryGetProperty(parameterName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static bool TryGetRequiredGuid(JsonElement parameters, string parameterName, out Guid value, out string error)
    {
        value = Guid.Empty;

        if (!TryGetRequiredString(parameters, parameterName, out var rawValue, out error))
            return false;

        if (!Guid.TryParse(rawValue, out value))
        {
            error = $"Invalid {parameterName}";
            return false;
        }

        return true;
    }

    private static bool TryGetRequiredInt32(JsonElement parameters, string parameterName, out int value, out string error)
    {
        value = 0;
        error = string.Empty;

        if (!parameters.TryGetProperty(parameterName, out var property))
        {
            error = $"Missing required parameter '{parameterName}'";
            return false;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out value))
        {
            error = $"Parameter '{parameterName}' must be an integer";
            return false;
        }

        return true;
    }

    private static bool TryGetGuidFromParameters(JsonElement parameters, string parameterName, out Guid value)
    {
        value = Guid.Empty;

        if (!parameters.TryGetProperty(parameterName, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        var raw = property.GetString();
        return Guid.TryParse(raw, out value);
    }

    private async Task<Result> SyncLinkedCaptureConversionAsync(ProposalDto proposal, CancellationToken cancellationToken)
    {
        if (proposal.SourceType != ProposalSourceType.Queue ||
            string.IsNullOrWhiteSpace(proposal.SourceReferenceId) ||
            !Guid.TryParse(proposal.SourceReferenceId, out var sourceRequestId))
        {
            return Result.Success();
        }

        var captureItem = await _unitOfWork.LlmQueue.GetByIdAsync(sourceRequestId, cancellationToken);
        if (captureItem == null || !CaptureRequestContract.IsCaptureRequestType(captureItem.RequestType))
        {
            return Result.Success();
        }

        var payloadResult = CaptureRequestContract.ParsePayload(captureItem.Payload, allowServerAttributionFields: true);
        if (!payloadResult.IsSuccess)
        {
            _logger?.LogWarning(
                "Automation proposal {ProposalId} applied but linked capture item {CaptureItemId} payload could not be parsed for conversion sync: {ErrorCode} {ErrorMessage}",
                proposal.Id,
                captureItem.Id,
                payloadResult.ErrorCode,
                payloadResult.ErrorMessage);
            return Result.Success();
        }

        var provenance = payloadResult.Value.Provenance;
        if (provenance?.ProposalId is { } linkedProposalId &&
            linkedProposalId != Guid.Empty &&
            linkedProposalId != proposal.Id)
        {
            _logger?.LogWarning(
                "Automation proposal {ProposalId} skipped capture conversion sync because linked capture item {CaptureItemId} already points at proposal {LinkedProposalId}",
                proposal.Id,
                captureItem.Id,
                linkedProposalId);
            return Result.Success();
        }

        if (provenance?.ConvertedAt is not null)
        {
            return Result.Success();
        }

        var convertedAt = provenance?.ConvertedAt ?? ResolveConvertedAt(proposal.AppliedAt);
        var updatedPayload = CaptureRequestContract.WithProvenance(
            payloadResult.Value,
            captureItem.Id,
            proposalId: proposal.Id,
            boardId: captureItem.BoardId ?? proposal.BoardId,
            convertedAt: convertedAt);

        try
        {
            captureItem.UpdatePayload(CaptureRequestContract.SerializePayload(updatedPayload));
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure(ErrorCodes.UnexpectedError, ex.Message);
        }
    }

    private static DateTimeOffset ResolveConvertedAt(DateTime? appliedAt)
    {
        if (!appliedAt.HasValue)
        {
            return DateTimeOffset.UtcNow;
        }

        var normalized = appliedAt.Value.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(appliedAt.Value, DateTimeKind.Utc),
            DateTimeKind.Local => appliedAt.Value.ToUniversalTime(),
            _ => appliedAt.Value
        };

        return new DateTimeOffset(normalized);
    }

    private async Task<Result> UpdateProposalStatusAsync(Guid proposalId, ProposalStatus status, string? failureReason, CancellationToken cancellationToken)
    {
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(proposalId, cancellationToken);
        if (proposal == null)
            return Result.Failure(ErrorCodes.NotFound, $"Proposal with ID {proposalId} not found");

        try
        {
            if (status == ProposalStatus.Applied)
            {
                proposal.MarkAsApplied();
            }
            else if (status == ProposalStatus.Failed)
            {
                proposal.MarkAsFailed(failureReason ?? "Unknown error");
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.ErrorCode, ex.Message);
        }
    }
}
