using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class BoardAccessService : IBoardAccessService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly DevelopmentSandboxSettings _sandboxSettings;
    private readonly INotificationService _notificationService;

    public BoardAccessService(
        IUnitOfWork unitOfWork,
        DevelopmentSandboxSettings? sandboxSettings = null,
        INotificationService? notificationService = null)
    {
        _unitOfWork = unitOfWork;
        _sandboxSettings = sandboxSettings ?? new DevelopmentSandboxSettings();
        _notificationService = notificationService ?? NoOpNotificationService.Instance;
    }

    public async Task<Result<BoardAccessDto>> GrantAccessAsync(GrantAccessDto dto, Guid grantedBy)
    {
        try
        {
            var board = await _unitOfWork.Boards.GetByIdAsync(dto.BoardId);
            if (board == null)
                return Result.Failure<BoardAccessDto>(ErrorCodes.NotFound, $"Board with ID {dto.BoardId} not found");

            var grantingUser = await _unitOfWork.Users.GetByIdAsync(grantedBy);
            if (grantingUser == null)
                return Result.Failure<BoardAccessDto>(ErrorCodes.NotFound, $"Granting user with ID {grantedBy} not found");

            var canManage = await EnsureCanManageBoardAccessAsync(board, grantedBy);
            if (!canManage.IsSuccess)
                return Result.Failure<BoardAccessDto>(canManage.ErrorCode, canManage.ErrorMessage);

            var user = await _unitOfWork.Users.GetByIdAsync(dto.UserId);
            if (user == null)
                return Result.Failure<BoardAccessDto>(ErrorCodes.NotFound, $"User with ID {dto.UserId} not found");

            var existingAccess = await _unitOfWork.BoardAccesses.GetByBoardAndUserAsync(dto.BoardId, dto.UserId);
            if (existingAccess != null)
                return Result.Failure<BoardAccessDto>(ErrorCodes.Conflict, $"User already has access to this board");

            var access = new BoardAccess(dto.BoardId, dto.UserId, dto.Role, grantedBy);
            await _unitOfWork.BoardAccesses.AddAsync(access);

            var notificationResult = await _notificationService.PublishAsync(
                new CreateNotificationRequestDto(
                    dto.UserId,
                    NotificationType.Assignment,
                    "Board access granted",
                    $"You were granted {dto.Role} access to board '{board.Name}'.",
                    dto.BoardId,
                    SourceEntityType: "board-access",
                    SourceEntityId: access.Id,
                    DeduplicationKey: $"assignment:grant:{dto.BoardId}:{dto.UserId}:{dto.Role}"));
            if (!notificationResult.IsSuccess)
                return Result.Failure<BoardAccessDto>(notificationResult.ErrorCode, notificationResult.ErrorMessage);

            await _unitOfWork.SaveChangesAsync();

            return Result.Success(MapToDto(access));
        }
        catch (DomainException ex)
        {
            return Result.Failure<BoardAccessDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<BoardAccessDto>> UpdateAccessAsync(Guid boardId, Guid accessId, UpdateAccessDto dto, Guid updatedBy)
    {
        try
        {
            var access = await _unitOfWork.BoardAccesses.GetByIdAsync(accessId);
            if (access == null || access.BoardId != boardId)
                return Result.Failure<BoardAccessDto>(ErrorCodes.NotFound, $"Board access with ID {accessId} not found");

            var board = await _unitOfWork.Boards.GetByIdAsync(boardId);
            if (board == null)
                return Result.Failure<BoardAccessDto>(ErrorCodes.NotFound, $"Board with ID {boardId} not found");

            var updatingUser = await _unitOfWork.Users.GetByIdAsync(updatedBy);
            if (updatingUser == null)
                return Result.Failure<BoardAccessDto>(ErrorCodes.NotFound, $"Updating user with ID {updatedBy} not found");

            var canManage = await EnsureCanManageBoardAccessAsync(board, updatedBy);
            if (!canManage.IsSuccess)
                return Result.Failure<BoardAccessDto>(canManage.ErrorCode, canManage.ErrorMessage);

            access.UpdateRole(dto.Role, updatedBy);

            var notificationResult = await _notificationService.PublishAsync(
                new CreateNotificationRequestDto(
                    access.UserId,
                    NotificationType.Assignment,
                    "Board access role updated",
                    $"Your role for board '{board.Name}' is now {dto.Role}.",
                    boardId,
                    SourceEntityType: "board-access",
                    SourceEntityId: access.Id,
                    DeduplicationKey: $"assignment:update:{boardId}:{access.UserId}:{dto.Role}"));
            if (!notificationResult.IsSuccess)
                return Result.Failure<BoardAccessDto>(notificationResult.ErrorCode, notificationResult.ErrorMessage);

            await _unitOfWork.SaveChangesAsync();

            return Result.Success(MapToDto(access));
        }
        catch (DomainException ex)
        {
            return Result.Failure<BoardAccessDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result> RevokeAccessAsync(Guid boardId, Guid accessId, Guid revokedBy)
    {
        var access = await _unitOfWork.BoardAccesses.GetByIdAsync(accessId);
        if (access == null || access.BoardId != boardId)
            return Result.Failure(ErrorCodes.NotFound, $"Board access with ID {accessId} not found");

        var board = await _unitOfWork.Boards.GetByIdAsync(boardId);
        if (board == null)
            return Result.Failure(ErrorCodes.NotFound, $"Board with ID {boardId} not found");

        var revokingUser = await _unitOfWork.Users.GetByIdAsync(revokedBy);
        if (revokingUser == null)
            return Result.Failure(ErrorCodes.NotFound, $"Revoking user with ID {revokedBy} not found");

        var canManage = await EnsureCanManageBoardAccessAsync(board, revokedBy);
        if (!canManage.IsSuccess)
            return Result.Failure(canManage.ErrorCode, canManage.ErrorMessage);

        await _unitOfWork.BoardAccesses.DeleteAsync(access);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result<IEnumerable<BoardAccessDto>>> GetBoardAccessListAsync(Guid boardId)
    {
        var board = await _unitOfWork.Boards.GetByIdAsync(boardId);
        if (board == null)
            return Result.Failure<IEnumerable<BoardAccessDto>>(ErrorCodes.NotFound, $"Board with ID {boardId} not found");

        var accesses = await _unitOfWork.BoardAccesses.GetByBoardIdAsync(boardId);
        return Result.Success(accesses.Select(MapToDto));
    }

    public async Task<Result<IEnumerable<BoardDto>>> GetUserBoardsAsync(Guid userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null)
            return Result.Failure<IEnumerable<BoardDto>>(ErrorCodes.NotFound, $"User with ID {userId} not found");

        var accesses = await _unitOfWork.BoardAccesses.GetByUserIdAsync(userId);
        return Result.Success(accesses.Select(a => MapToBoardDto(a.Board)));
    }

    private async Task<Result> EnsureCanManageBoardAccessAsync(Board board, Guid actingUserId)
    {
        if (_sandboxSettings.Enabled)
            return Result.Success();

        if (board.OwnerId == actingUserId)
            return Result.Success();

        if (board.OwnerId is null)
        {
            // Transitional bootstrap for legacy ownerless boards:
            // first manager claim assigns ownership to the acting user.
            var existingAccesses = await _unitOfWork.BoardAccesses.GetByBoardIdAsync(board.Id);
            if (!existingAccesses.Any())
            {
                board.TransferOwnership(actingUserId);
                return Result.Success();
            }
        }

        var actingAccess = await _unitOfWork.BoardAccesses.GetByBoardAndUserAsync(board.Id, actingUserId);
        if (actingAccess == null || !actingAccess.CanManageAccess())
            return Result.Failure(ErrorCodes.Forbidden, "You do not have permission to manage board access");

        return Result.Success();
    }

    private static BoardAccessDto MapToDto(BoardAccess access)
    {
        return new BoardAccessDto(
            access.Id,
            access.BoardId,
            access.UserId,
            access.Role,
            access.GrantedBy,
            access.GrantedAt);
    }

    private static BoardDto MapToBoardDto(Board board)
    {
        return new BoardDto(
            board.Id,
            board.Name,
            board.Description,
            board.IsArchived,
            board.CreatedAt,
            board.UpdatedAt);
    }
}
