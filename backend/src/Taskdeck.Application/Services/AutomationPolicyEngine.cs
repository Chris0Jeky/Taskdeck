using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class AutomationPolicyEngine : IAutomationPolicyEngine
{
    private readonly IUnitOfWork _unitOfWork;
    private const int MaxOperationCount = 50;
    private const int MaxParametersLength = 10000;

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

    public async Task<Result> ValidatePermissionsAsync(Guid userId, Guid? boardId, IEnumerable<ProposalOperationDto> operations, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure(ErrorCodes.ValidationError, "UserId cannot be empty");

        // Verify user exists
        var user = await _unitOfWork.Users.GetByIdAsync(userId, cancellationToken);
        if (user == null)
            return Result.Failure(ErrorCodes.NotFound, $"User with ID {userId} not found");

        var opList = operations.ToList();
        if (!opList.Any())
            return Result.Success();

        // If board-scoped, verify board exists and user has access
        if (boardId.HasValue)
        {
            var board = await _unitOfWork.Boards.GetByIdAsync(boardId.Value, cancellationToken);
            if (board == null)
                return Result.Failure(ErrorCodes.NotFound, $"Board with ID {boardId} not found");

            var hasAccess = await _unitOfWork.BoardAccesses.HasAccessAsync(boardId.Value, userId, null, cancellationToken);
            if (!hasAccess)
                return Result.Failure(ErrorCodes.Forbidden, $"User does not have access to board {boardId}");
        }

        // Validate each operation targets entities within the board scope
        foreach (var operation in opList)
        {
            if (boardId.HasValue && operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(operation.TargetId))
            {
                if (Guid.TryParse(operation.TargetId, out var cardId))
                {
                    var card = await _unitOfWork.Cards.GetByIdAsync(cardId, cancellationToken);
                    if (card != null && card.BoardId != boardId.Value)
                        return Result.Failure(ErrorCodes.Forbidden, $"Card {cardId} does not belong to board {boardId}");
                }
            }
        }

        return Result.Success();
    }

    public Result ValidatePolicy(ProposalDto proposal)
    {
        if (proposal == null)
            return Result.Failure(ErrorCodes.ValidationError, "Proposal cannot be null");

        if (proposal.Operations == null || !proposal.Operations.Any())
            return Result.Failure(ErrorCodes.ValidationError, "Proposal must contain at least one operation");

        if (proposal.Operations.Count > MaxOperationCount)
            return Result.Failure(ErrorCodes.ValidationError, $"Proposal exceeds maximum operation count of {MaxOperationCount}");

        // Validate operation sequences are unique and non-negative
        var sequences = proposal.Operations.Select(o => o.Sequence).ToList();
        if (sequences.Distinct().Count() != sequences.Count)
            return Result.Failure(ErrorCodes.ValidationError, "Operation sequences must be unique");

        if (sequences.Any(s => s < 0))
            return Result.Failure(ErrorCodes.ValidationError, "Operation sequences must be non-negative");

        // Validate parameters size
        foreach (var operation in proposal.Operations)
        {
            if (operation.Parameters.Length > MaxParametersLength)
                return Result.Failure(ErrorCodes.ValidationError, $"Operation parameters exceed maximum length of {MaxParametersLength}");
        }

        // Validate proposal hasn't expired
        if (DateTime.UtcNow > proposal.ExpiresAt)
            return Result.Failure(ErrorCodes.ValidationError, "Proposal has expired");

        return Result.Success();
    }
}
