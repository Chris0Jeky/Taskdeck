using System.Text.Json;
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
    private readonly Dictionary<string, HashSet<string>> _executedOperations = new();

    public AutomationExecutorService(
        IUnitOfWork unitOfWork,
        IAutomationProposalService proposalService,
        IAutomationPolicyEngine policyEngine,
        CardService cardService,
        BoardService boardService,
        ColumnService columnService)
    {
        _unitOfWork = unitOfWork;
        _proposalService = proposalService;
        _policyEngine = policyEngine;
        _cardService = cardService;
        _boardService = boardService;
        _columnService = columnService;
    }

    public async Task<Result> ExecuteProposalAsync(Guid proposalId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (proposalId == Guid.Empty)
            return Result.Failure(ErrorCodes.ValidationError, "ProposalId cannot be empty");

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Result.Failure(ErrorCodes.ValidationError, "IdempotencyKey cannot be empty");

        // Check idempotency - has this proposal been executed with this key?
        if (_executedOperations.ContainsKey(idempotencyKey) && 
            _executedOperations[idempotencyKey].Contains(proposalId.ToString()))
        {
            return Result.Success(); // Already executed, return success
        }

        // Get proposal
        var proposalResult = await _proposalService.GetProposalByIdAsync(proposalId, cancellationToken);
        if (!proposalResult.IsSuccess)
            return Result.Failure(proposalResult.ErrorCode, proposalResult.ErrorMessage);

        var proposal = proposalResult.Value;

        // Verify proposal is approved
        if (proposal.Status != ProposalStatus.Approved)
            return Result.Failure(ErrorCodes.InvalidOperation, $"Cannot execute proposal in status {proposal.Status}");

        // Revalidate policy before execution
        var policyResult = _policyEngine.ValidatePolicy(proposal);
        if (!policyResult.IsSuccess)
            return Result.Failure(policyResult.ErrorCode, policyResult.ErrorMessage);

        // Revalidate permissions
        var permissionResult = await _policyEngine.ValidatePermissionsAsync(
            proposal.RequestedByUserId, 
            proposal.BoardId, 
            proposal.Operations, 
            cancellationToken);
        if (!permissionResult.IsSuccess)
            return Result.Failure(permissionResult.ErrorCode, permissionResult.ErrorMessage);

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            // Execute operations in sequence order
            var orderedOperations = proposal.Operations.OrderBy(o => o.Sequence).ToList();
            var failedOperation = -1;
            var failureReason = "";

            foreach (var operation in orderedOperations)
            {
                var executionResult = await ExecuteOperationAsync(operation, proposal.RequestedByUserId, cancellationToken);
                if (!executionResult.IsSuccess)
                {
                    failedOperation = operation.Sequence;
                    failureReason = $"Operation {operation.Sequence} failed: {executionResult.ErrorMessage}";
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

                return Result.Failure(ErrorCodes.UnexpectedError, failureReason);
            }

            // Mark proposal as applied
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            
            var markResult = await UpdateProposalStatusAsync(proposalId, ProposalStatus.Applied, null, cancellationToken);
            if (!markResult.IsSuccess)
                return Result.Failure(markResult.ErrorCode, markResult.ErrorMessage);

            // Record idempotency
            if (!_executedOperations.ContainsKey(idempotencyKey))
                _executedOperations[idempotencyKey] = new HashSet<string>();
            _executedOperations[idempotencyKey].Add(proposalId.ToString());

            return Result.Success();
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            await UpdateProposalStatusAsync(proposalId, ProposalStatus.Failed, ex.Message, cancellationToken);
            return Result.Failure(ErrorCodes.UnexpectedError, $"Failed to execute proposal: {ex.Message}");
        }
    }

    private async Task<Result> ExecuteOperationAsync(ProposalOperationDto operation, Guid userId, CancellationToken cancellationToken)
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
        var parameters = JsonSerializer.Deserialize<JsonElement>(operation.Parameters);

        switch (actionType)
        {
            case "create":
                return await CreateCardAsync(parameters, cancellationToken);
            
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

    private async Task<Result> CreateCardAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var title = parameters.GetProperty("title").GetString();
        var description = parameters.TryGetProperty("description", out var descProp) ? descProp.GetString() : null;
        var columnIdStr = parameters.GetProperty("columnId").GetString();
        var boardIdStr = parameters.GetProperty("boardId").GetString();

        if (!Guid.TryParse(columnIdStr, out var columnId))
            return Result.Failure(ErrorCodes.ValidationError, "Invalid columnId");
        
        if (!Guid.TryParse(boardIdStr, out var boardId))
            return Result.Failure(ErrorCodes.ValidationError, "Invalid boardId");

        var dto = new CreateCardDto(boardId, columnId, title!, description, null, null);
        var result = await _cardService.CreateCardAsync(dto, cancellationToken);
        
        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result> UpdateCardAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var cardIdStr = parameters.GetProperty("cardId").GetString();
        if (!Guid.TryParse(cardIdStr, out var cardId))
            return Result.Failure(ErrorCodes.ValidationError, "Invalid cardId");

        var title = parameters.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
        var description = parameters.TryGetProperty("description", out var descProp) ? descProp.GetString() : null;

        var dto = new UpdateCardDto(title, description, null, null, null, null);
        var result = await _cardService.UpdateCardAsync(cardId, dto, cancellationToken);
        
        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result> MoveCardAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var cardIdStr = parameters.GetProperty("cardId").GetString();
        var columnIdStr = parameters.GetProperty("columnId").GetString();

        if (!Guid.TryParse(cardIdStr, out var cardId))
            return Result.Failure(ErrorCodes.ValidationError, "Invalid cardId");
        
        if (!Guid.TryParse(columnIdStr, out var columnId))
            return Result.Failure(ErrorCodes.ValidationError, "Invalid columnId");

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
        var cardIdStr = parameters.GetProperty("cardId").GetString();
        if (!Guid.TryParse(cardIdStr, out var cardId))
            return Result.Failure(ErrorCodes.ValidationError, "Invalid cardId");

        var dto = new UpdateCardDto(null, null, null, true, null, null);
        var result = await _cardService.UpdateCardAsync(cardId, dto, cancellationToken);
        
        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result> ExecuteBoardOperationAsync(string actionType, ProposalOperationDto operation, CancellationToken cancellationToken)
    {
        var parameters = JsonSerializer.Deserialize<JsonElement>(operation.Parameters);

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
        var boardIdStr = parameters.GetProperty("boardId").GetString();
        if (!Guid.TryParse(boardIdStr, out var boardId))
            return Result.Failure(ErrorCodes.ValidationError, "Invalid boardId");

        var name = parameters.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
        var description = parameters.TryGetProperty("description", out var descProp) ? descProp.GetString() : null;

        var dto = new UpdateBoardDto(name, description, null);
        var result = await _boardService.UpdateBoardAsync(boardId, dto, cancellationToken);
        
        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode, result.ErrorMessage);
    }

    private async Task<Result> ExecuteColumnOperationAsync(string actionType, ProposalOperationDto operation, CancellationToken cancellationToken)
    {
        var parameters = JsonSerializer.Deserialize<JsonElement>(operation.Parameters);

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
        var columnIdStr = parameters.GetProperty("columnId").GetString();
        var newPosition = parameters.GetProperty("position").GetInt32();

        if (!Guid.TryParse(columnIdStr, out var columnId))
            return Result.Failure(ErrorCodes.ValidationError, "Invalid columnId");

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
            { "move", AuditAction.Moved }
        };

        var auditAction = actionMap.ContainsKey(operation.ActionType.ToLowerInvariant()) 
            ? actionMap[operation.ActionType.ToLowerInvariant()] 
            : AuditAction.Updated;

        var entityId = !string.IsNullOrEmpty(operation.TargetId) && Guid.TryParse(operation.TargetId, out var id)
            ? id
            : Guid.NewGuid(); // For creates, we'd need to capture the created ID

        var changes = $"Automation Proposal {proposal.Id}: {operation.ActionType} {operation.TargetType}. Parameters: {operation.Parameters}";

        var auditLog = new AuditLog(
            operation.TargetType,
            entityId,
            auditAction,
            proposal.RequestedByUserId,
            changes
        );

        await _unitOfWork.AuditLogs.AddAsync(auditLog, cancellationToken);
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
