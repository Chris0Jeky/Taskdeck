using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class AutomationPolicyEngine : IAutomationPolicyEngine
{
    private readonly IUnitOfWork _unitOfWork;

    public AutomationPolicyEngine(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public RiskLevel ClassifyRisk(IEnumerable<ProposalOperationDto> operations)
    {
        var opList = operations.ToList();
        
        if (!opList.Any())
            return RiskLevel.Low;

        var hasDelete = opList.Any(o => o.ActionType.Contains("delete", StringComparison.OrdinalIgnoreCase));
        var hasArchive = opList.Any(o => o.ActionType.Contains("archive", StringComparison.OrdinalIgnoreCase));
        var hasUpdate = opList.Any(o => o.ActionType.Contains("update", StringComparison.OrdinalIgnoreCase));
        var hasBoardOperation = opList.Any(o => o.TargetType.Equals("board", StringComparison.OrdinalIgnoreCase));
        var operationCount = opList.Count;

        // Critical: Delete board or many operations
        if (hasBoardOperation && hasDelete)
            return RiskLevel.Critical;
        
        if (operationCount > 20)
            return RiskLevel.Critical;

        // High: Delete operations, board updates, or many operations
        if (hasDelete || (hasBoardOperation && hasUpdate))
            return RiskLevel.High;
        
        if (operationCount > 10)
            return RiskLevel.High;

        // Medium: Archive operations or moderate operation count
        if (hasArchive)
            return RiskLevel.Medium;
        
        if (operationCount > 5)
            return RiskLevel.Medium;

        // Low: Simple creates and updates with few operations
        return RiskLevel.Low;
    }

    public async Task<Result> ValidateBoardAccessAsync(Guid requesterUserId, Guid? boardId, CancellationToken cancellationToken = default)
    {
        if (requesterUserId == Guid.Empty)
            return Result.Failure(ErrorCodes.ValidationError, "UserId cannot be empty");

        // Verify user exists
        var user = await _unitOfWork.Users.GetByIdAsync(requesterUserId, cancellationToken);
        if (user == null)
            return Result.Failure(ErrorCodes.NotFound, $"User with ID {requesterUserId} not found");

        // If board-scoped, verify board exists and user has access
        if (boardId.HasValue)
        {
            var board = await _unitOfWork.Boards.GetByIdAsync(boardId.Value, cancellationToken);
            if (board == null)
                return Result.Failure(ErrorCodes.NotFound, $"Board with ID {boardId} not found");

            var hasAccess = await _unitOfWork.BoardAccesses.HasAccessAsync(boardId.Value, requesterUserId, null, cancellationToken);
            if (!hasAccess)
                return Result.Failure(ErrorCodes.Forbidden, $"User does not have access to board {boardId}");
        }

        return Result.Success();
    }

    public async Task<Result> ValidatePermissionsAsync(Guid userId, Guid? boardId, IEnumerable<ProposalOperationDto> operations, CancellationToken cancellationToken = default)
    {
        var opList = operations.ToList();

        // The requester half of the shared access gate always runs; the board half applies only
        // when the proposal carries operations, preserving the long-standing empty-operations
        // short-circuit (empty → requester checks, then Success, board untouched). Callers that
        // must gate board access for an operation-less proposal (the terminal stored-preview
        // read, #1415) call ValidateBoardAccessAsync directly with the boardId.
        var accessValidation = await ValidateBoardAccessAsync(
            userId,
            opList.Count > 0 ? boardId : null,
            cancellationToken);
        if (!accessValidation.IsSuccess)
            return accessValidation;

        if (opList.Count == 0)
            return Result.Success();

        return await ProposalOperationContractValidator.ValidateAsync(
            _unitOfWork,
            boardId,
            opList,
            cancellationToken);
    }

    // Delegates to the shared structure validator so Apply, revision-save, and the
    // original-proposal diff all enforce the same operation-shape invariants (#1370).
    public Result ValidateOperationStructure(IReadOnlyCollection<ProposalOperationDto> operations)
        => ProposalOperationStructureValidator.Validate(operations);

    public Result ValidatePolicy(ProposalDto proposal)
    {
        if (proposal == null)
            return Result.Failure(ErrorCodes.ValidationError, "Proposal cannot be null");

        var structureValidation = ValidateOperationStructure(proposal.Operations);
        if (!structureValidation.IsSuccess)
            return structureValidation;

        // Validate proposal hasn't expired
        if (DateTime.UtcNow > proposal.ExpiresAt)
            return Result.Failure(ErrorCodes.ValidationError, "Proposal has expired");

        return Result.Success();
    }
}
