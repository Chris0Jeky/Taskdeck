using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class BoardAccessService : IBoardAccessService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;
    private readonly CardAssignmentService? _assignments;
    private readonly ICardAssignmentStore? _assignmentStore;

    // No DevelopmentSandboxSettings dependency: the development sandbox never widens write-class
    // authorization (ADR-0068 / #1866). Board-access management stays owner-or-manager only.
    public BoardAccessService(
        IUnitOfWork unitOfWork,
        INotificationService? notificationService = null,
        CardAssignmentService? assignments = null,
        ICardAssignmentStore? assignmentStore = null)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService ?? NoOpNotificationService.Instance;
        _assignments = assignments;
        _assignmentStore = assignmentStore;
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

            // Ownership transfer is owner-only: an Admin must not escalate anyone
            // (including themselves) to Owner. Checked before grantee resolution so
            // a rejected grant performs no user lookups.
            var canGrantRole = await EnsureCanGrantRoleAsync(board, grantedBy, dto.Role);
            if (!canGrantRole.IsSuccess)
                return Result.Failure<BoardAccessDto>(canGrantRole.ErrorCode, canGrantRole.ErrorMessage);

            // Resolve the grantee only after the manage-access gate passes. An email-or-username
            // identifier takes precedence over the raw UserId compatibility path. Unknown
            // identifiers return a uniform NotFound that never echoes the supplied identifier, so
            // the grant path cannot be used to enumerate which users exist beyond the grant result.
            Guid targetUserId;
            if (!string.IsNullOrWhiteSpace(dto.Identifier))
            {
                var resolvedUser = await ResolveUserByIdentifierAsync(dto.Identifier);
                if (resolvedUser == null)
                    return Result.Failure<BoardAccessDto>(ErrorCodes.NotFound, "User not found");
                targetUserId = resolvedUser.Id;
            }
            else
            {
                var user = await _unitOfWork.Users.GetByIdAsync(dto.UserId);
                if (user == null)
                    return Result.Failure<BoardAccessDto>(ErrorCodes.NotFound, "User not found");
                targetUserId = dto.UserId;
            }

            var existingAccess = await _unitOfWork.BoardAccesses.GetByBoardAndUserAsync(dto.BoardId, targetUserId);
            if (existingAccess != null)
                return Result.Failure<BoardAccessDto>(ErrorCodes.Conflict, $"User already has access to this board");

            var access = new BoardAccess(dto.BoardId, targetUserId, dto.Role, grantedBy);
            await _unitOfWork.BoardAccesses.AddAsync(access);

            var notificationResult = await _notificationService.PublishAsync(
                new CreateNotificationRequestDto(
                    targetUserId,
                    NotificationType.Assignment,
                    "Board access granted",
                    $"You were granted {dto.Role} access to board '{board.Name}'.",
                    dto.BoardId,
                    SourceEntityType: "board-access",
                    SourceEntityId: access.Id,
                    DeduplicationKey: $"assignment:grant:{dto.BoardId}:{targetUserId}:{dto.Role}"));
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

            var canManageExisting = await EnsureCanManageExistingAccessAsync(board, access, updatedBy);
            if (!canManageExisting.IsSuccess)
                return Result.Failure<BoardAccessDto>(canManageExisting.ErrorCode, canManageExisting.ErrorMessage);

            // Same ownership-transfer bar as the grant path: only an effective
            // owner may move a row to the Owner role.
            var canGrantRole = await EnsureCanGrantRoleAsync(board, updatedBy, dto.Role);
            if (!canGrantRole.IsSuccess)
                return Result.Failure<BoardAccessDto>(canGrantRole.ErrorCode, canGrantRole.ErrorMessage);

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
        await _unitOfWork.BeginTransactionAsync();
        try
        {
            if (_assignmentStore is not null) await _assignmentStore.RefreshAuthorityAsync(boardId, revokedBy, default);
            var result = await StageRevokeAccessAsync(boardId, accessId, revokedBy);
            if (!result.IsSuccess) { await _unitOfWork.RollbackTransactionAsync(); return result; }
            await _unitOfWork.CommitTransactionAsync();
            if (_assignments is not null)
                foreach (var cardId in result.Value)
                    await _assignments.NotifyAsync(boardId, cardId);
            return result;
        }
        catch { await _unitOfWork.RollbackTransactionAsync(); throw; }
    }

    private async Task<Result<IReadOnlyList<Guid>>> StageRevokeAccessAsync(Guid boardId, Guid accessId, Guid revokedBy)
    {
        var access = await _unitOfWork.BoardAccesses.GetByIdAsync(accessId);
        if (access == null || access.BoardId != boardId)
            return Result.Failure<IReadOnlyList<Guid>>(ErrorCodes.NotFound, $"Board access with ID {accessId} not found");

        var board = await _unitOfWork.Boards.GetByIdAsync(boardId);
        if (board == null)
            return Result.Failure<IReadOnlyList<Guid>>(ErrorCodes.NotFound, $"Board with ID {boardId} not found");

        var revokingUser = await _unitOfWork.Users.GetByIdAsync(revokedBy);
        if (revokingUser == null)
            return Result.Failure<IReadOnlyList<Guid>>(ErrorCodes.NotFound, $"Revoking user with ID {revokedBy} not found");

        var canManage = await EnsureCanManageBoardAccessAsync(board, revokedBy);
        if (!canManage.IsSuccess)
            return Result.Failure<IReadOnlyList<Guid>>(canManage.ErrorCode, canManage.ErrorMessage);

        var canManageExisting = await EnsureCanManageExistingAccessAsync(board, access, revokedBy);
        if (!canManageExisting.IsSuccess)
            return Result.Failure<IReadOnlyList<Guid>>(canManageExisting.ErrorCode, canManageExisting.ErrorMessage);

        IReadOnlyList<Card> detachedCards = [];
        if (board.OwnerId != access.UserId && _assignments is not null)
            detachedCards = await _assignments.StageDetachAsync(access.UserId, boardId, revokedBy, "access-revoked");
        board.RecordHierarchyMutation();
        await _unitOfWork.BoardAccesses.DeleteAsync(access);
        await _unitOfWork.SaveChangesAsync();

        return Result.Success<IReadOnlyList<Guid>>(detachedCards.Select(card => card.Id).ToArray());
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
        // Every row here belongs to `userId`, so its role IS that user's write capability —
        // no extra lookup needed, and no board is stamped `canWrite: false` by omission.
        return Result.Success(accesses.Select(a => MapToBoardDto(a.Board, a.CanWrite())));
    }

    /// <summary>
    /// Resolves an email-or-username identifier to a user without exposing a user directory.
    /// An identifier containing '@' is treated as an email (emails always contain '@' per the
    /// User domain invariant); otherwise it is treated as a username. Returns null when no user
    /// matches, so callers can surface a uniform NotFound.
    /// </summary>
    private async Task<User?> ResolveUserByIdentifierAsync(string identifier)
    {
        var trimmed = identifier.Trim();
        if (trimmed.Contains('@'))
            return await _unitOfWork.Users.GetByEmailAsync(trimmed);

        return await _unitOfWork.Users.GetByUsernameAsync(trimmed);
    }

    private Task<Result> EnsureCanManageExistingAccessAsync(Board board, BoardAccess access, Guid actingUserId)
    {
        // Protect the effective role before mutation, not only the requested new role.
        // The primary owner stays an Owner even if a redundant access row says otherwise.
        var effectiveRole = access.UserId == board.OwnerId ? UserRole.Owner : access.Role;
        return EnsureCanGrantRoleAsync(board, actingUserId, effectiveRole);
    }

    /// <summary>
    /// Enforces the grant hierarchy: only an effective board owner (the board's
    /// <c>OwnerId</c> or a holder of an Owner access row) may grant or assign the
    /// Owner role. Admins keep manage-access rights for roles at or below their
    /// own (Admin/Editor/Viewer), matching the UserRole.Admin contract
    /// ("except ownership transfer").
    /// </summary>
    private async Task<Result> EnsureCanGrantRoleAsync(Board board, Guid actingUserId, UserRole targetRole)
    {
        if (targetRole != UserRole.Owner)
            return Result.Success();

        if (board.OwnerId == actingUserId)
            return Result.Success();

        var actingAccess = await _unitOfWork.BoardAccesses.GetByBoardAndUserAsync(board.Id, actingUserId);
        if (actingAccess?.Role == UserRole.Owner)
            return Result.Success();

        return Result.Failure(ErrorCodes.Forbidden, "Only board owners can assign the Owner role");
    }

    private async Task<Result> EnsureCanManageBoardAccessAsync(Board board, Guid actingUserId)
    {
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

    private static BoardDto MapToBoardDto(Board board, bool canWrite)
    {
        return new BoardDto(
            board.Id,
            board.Name,
            board.Description,
            board.IsArchived,
            board.CreatedAt,
            board.UpdatedAt,
            canWrite);
    }
}
