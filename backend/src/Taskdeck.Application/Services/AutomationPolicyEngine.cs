using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class AutomationPolicyEngine : IAutomationPolicyEngine
{
    private const string ArchivedProposalDecisionMessage =
        "Cannot modify proposals on an archived board. Restore the board before changing its decision history.";

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

    public async Task<Result> ValidateBoardAccessAsync(
        Guid requesterUserId,
        Guid? boardId,
        BoardAccessBar accessBar,
        CancellationToken cancellationToken = default)
    {
        if (requesterUserId == Guid.Empty)
            return Result.Failure(ErrorCodes.ValidationError, "UserId cannot be empty");

        // Verify user exists
        var user = await _unitOfWork.Users.GetByIdAsync(requesterUserId, cancellationToken);
        if (user == null)
            return Result.Failure(ErrorCodes.NotFound, $"User with ID {requesterUserId} not found");

        // If board-scoped, verify the board exists and the requester clears the bar THIS LANE asks
        // for. The bar is split (#1836) because one gate serves two kinds of caller:
        //
        //   Write — proposal creation (planner/worker/chat/tool lanes), approve, and execute. These
        //     mutate the board, so read-only membership must be refused. This is a defense-in-depth
        //     MIRROR: the independent enforcement is the API-side #1794/#1827 bar,
        //     AuthorizationService.CanWriteBoardAsync on the capture/proposal endpoints, which is a
        //     different component on a different layer. It is NOT a second check inside this engine
        //     — this method is the engine's only board-access gate — so "would fail at execute
        //     anyway" means "the API-side CanWriteBoardAsync bar refuses it", not "another policy
        //     check here would". Mirroring it fails the request earlier, with a clearer message,
        //     and covers the worker lane, which does not pass through those endpoints at all.
        //     UserRole.Editor is the exact membership set BoardAccess.CanWrite() admits (Owner,
        //     Admin, Editor) plus the board owner, whom HasAccessAsync short-circuits separately.
        //
        //   Read — the pending-proposal diff and the terminal stored-preview read behind MCP
        //     proposal_detail (#1415). Reading the detail of a proposal you authored is not a
        //     mutation, so it stays on plain membership (the pre-#1836 bar). Applying the write bar
        //     here regressed a board member demoted to Viewer out of their OWN proposals' detail —
        //     ProposalResources.GetProposalDetail throws on a failed preview, so the whole resource
        //     was lost, not just the preview field (coordinator ruling amendment on #1836).
        //
        // Behaviour note for the write lanes: a capture enqueued BEFORE #1827 by a read-only member
        // reaches CaptureTriageService with a board it may no longer target. It is rejected here —
        // as an explicit Result outcome propagated by the caller, never thrown (the LLM-lane
        // convention). That is the intended tightening.
        if (boardId.HasValue)
        {
            var board = await _unitOfWork.Boards.GetByIdAsync(boardId.Value, cancellationToken);
            if (board == null)
                return Result.Failure(ErrorCodes.NotFound, $"Board with ID {boardId} not found");

            var hasAccess = await _unitOfWork.BoardAccesses.HasAccessAsync(
                boardId.Value,
                requesterUserId,
                ResolveMinimumRole(accessBar),
                cancellationToken);
            if (!hasAccess)
                return Result.Failure(
                    ErrorCodes.Forbidden,
                    accessBar == BoardAccessBar.Read
                        ? $"User does not have access to board {boardId}"
                        : $"User does not have write access to board {boardId}");
        }

        return Result.Success();
    }

    public async Task<Result> GuardProposalDecisionWritesAsync(
        IEnumerable<Guid?> boardIds,
        CancellationToken cancellationToken = default)
    {
        var distinctBoardIds = boardIds
            .Where(boardId => boardId.HasValue)
            .Select(boardId => boardId!.Value)
            .Distinct()
            .ToList();

        if (distinctBoardIds.Count == 0)
            return Result.Success();

        var boards = (await _unitOfWork.Boards.GetByIdsAsync(distinctBoardIds, cancellationToken))
            .ToList();

        if (boards.Any(board => board.IsArchived))
            return Result.Failure(ErrorCodes.InvalidOperation, ArchivedProposalDecisionMessage);

        foreach (var board in boards)
            board.RecordDependentMutation();

        return Result.Success();
    }

    /// <summary>
    /// Maps a <see cref="BoardAccessBar"/> to the minimum role
    /// <c>IBoardAccessRepository.HasAccessAsync</c> takes — the single place the two bars are
    /// defined. <see cref="BoardAccessBar.Read"/> is <c>null</c> ("any membership"), which is the
    /// literal pre-#1836 argument rather than <c>UserRole.Viewer</c>: the two are equivalent for
    /// today's enum, but only <c>null</c> stays equivalent if a role below Viewer is ever added.
    /// An unrecognised bar falls back to the STRICTER write role so a future value added without
    /// an arm here fails closed.
    /// </summary>
    private static UserRole? ResolveMinimumRole(BoardAccessBar accessBar) => accessBar switch
    {
        BoardAccessBar.Read => null,
        _ => UserRole.Editor
    };

    public async Task<Result> ValidatePermissionsAsync(
        Guid userId,
        Guid? boardId,
        IEnumerable<ProposalOperationDto> operations,
        BoardAccessBar accessBar,
        CancellationToken cancellationToken = default)
    {
        if (operations is null)
            return Result.Failure(ErrorCodes.ValidationError, "Operations cannot be null");

        // The full requester/board access gate ALWAYS runs — including for an empty operation
        // list. Only the per-operation contract checks are operation-dependent; requester
        // existence and board access are not, so an operation-less proposal must be gated on the
        // board it targets exactly like an operation-bearing one. Emptiness itself is NOT rejected
        // here: it is a legitimate transient shape (a proposal may be created empty and revised
        // into validity, pinned by #1423), and the "nothing to apply" rejection belongs to the
        // structure gate (ValidateOperationStructure / ProposalOperationStructureValidator), which
        // runs BEFORE this method in every approve/apply/diff chain. Previously an empty list
        // short-circuited to Success with the board half skipped (boardId forced to null), which
        // silently treated an operation-less proposal as permitted and forced every new consumer
        // to bolt on its own board-access fallback (the #1415/#1425 trap this hardens away, #1426).
        var accessValidation = await ValidateBoardAccessAsync(
            userId,
            boardId,
            accessBar,
            cancellationToken);
        if (!accessValidation.IsSuccess)
            return accessValidation;

        // Materialize only after the access gate passes — the per-operation contract validator
        // below is the sole consumer of the list, so a failed access check pays no allocation.
        var opList = operations.ToList();
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
