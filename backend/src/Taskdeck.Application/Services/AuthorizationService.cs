using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class AuthorizationService : IAuthorizationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly DevelopmentSandboxSettings _sandboxSettings;

    public AuthorizationService(IUnitOfWork unitOfWork, DevelopmentSandboxSettings? sandboxSettings = null)
    {
        _unitOfWork = unitOfWork;
        _sandboxSettings = sandboxSettings ?? new DevelopmentSandboxSettings();
    }

    public async Task<Result<IReadOnlySet<Guid>>> GetReadableBoardIdsAsync(
        Guid userId,
        IEnumerable<Guid> boardIds,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<IReadOnlySet<Guid>>(ErrorCodes.ValidationError, "User ID cannot be empty");

        var candidateBoardIds = boardIds.Distinct().ToList();
        if (candidateBoardIds.Count == 0)
            return Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid>());

        if (_sandboxSettings.Enabled)
            return Result.Success<IReadOnlySet<Guid>>(candidateBoardIds.ToHashSet());

        var readableBoardIds = (await _unitOfWork.Boards.GetOwnedBoardIdsAsync(
                userId,
                candidateBoardIds,
                cancellationToken))
            .ToHashSet();

        if (readableBoardIds.Count == candidateBoardIds.Count)
            return Result.Success<IReadOnlySet<Guid>>(readableBoardIds);

        var boardAccesses = await _unitOfWork.BoardAccesses.GetByUserIdAsync(userId, cancellationToken);
        var candidateBoardIdSet = candidateBoardIds.ToHashSet();
        foreach (var boardAccess in boardAccesses)
        {
            if (candidateBoardIdSet.Contains(boardAccess.BoardId) && boardAccess.CanRead())
            {
                readableBoardIds.Add(boardAccess.BoardId);
            }
        }

        return Result.Success<IReadOnlySet<Guid>>(readableBoardIds);
    }

    public async Task<Result<bool>> CanReadBoardAsync(Guid userId, Guid boardId)
    {
        var board = await _unitOfWork.Boards.GetByIdAsync(boardId);
        if (board is null)
            return Result.Failure<bool>(ErrorCodes.NotFound, $"Board with ID {boardId} not found");

        if (_sandboxSettings.Enabled)
            return Result.Success(true);

        if (board.OwnerId == userId)
            return Result.Success(true);

        var access = await _unitOfWork.BoardAccesses.GetByBoardAndUserAsync(boardId, userId);
        return Result.Success(access is not null && access.CanRead());
    }

    public async Task<Result<bool>> CanWriteBoardAsync(Guid userId, Guid boardId)
    {
        var board = await _unitOfWork.Boards.GetByIdAsync(boardId);
        if (board is null)
            return Result.Failure<bool>(ErrorCodes.NotFound, $"Board with ID {boardId} not found");

        if (_sandboxSettings.Enabled)
            return Result.Success(true);

        if (board.OwnerId == userId)
            return Result.Success(true);

        var access = await _unitOfWork.BoardAccesses.GetByBoardAndUserAsync(boardId, userId);
        return Result.Success(access is not null && access.CanWrite());
    }

    public async Task<Result<bool>> CanManageBoardAccessAsync(Guid userId, Guid boardId)
    {
        var board = await _unitOfWork.Boards.GetByIdAsync(boardId);
        if (board is null)
            return Result.Failure<bool>(ErrorCodes.NotFound, $"Board with ID {boardId} not found");

        if (_sandboxSettings.Enabled)
            return Result.Success(true);

        if (board.OwnerId == userId)
            return Result.Success(true);

        var access = await _unitOfWork.BoardAccesses.GetByBoardAndUserAsync(boardId, userId);
        return Result.Success(access is not null && access.CanManageAccess());
    }

    public async Task<Result<bool>> CanDeleteBoardAsync(Guid userId, Guid boardId)
    {
        var board = await _unitOfWork.Boards.GetByIdAsync(boardId);
        if (board is null)
            return Result.Failure<bool>(ErrorCodes.NotFound, $"Board with ID {boardId} not found");

        if (_sandboxSettings.Enabled)
            return Result.Success(true);

        if (board.OwnerId == userId)
            return Result.Success(true);

        var access = await _unitOfWork.BoardAccesses.GetByBoardAndUserAsync(boardId, userId);
        return Result.Success(access is not null && access.CanDelete());
    }

    public async Task<Result<UserRole?>> GetUserRoleForBoardAsync(Guid userId, Guid boardId)
    {
        var board = await _unitOfWork.Boards.GetByIdAsync(boardId);
        if (board is null)
            return Result.Failure<UserRole?>(ErrorCodes.NotFound, $"Board with ID {boardId} not found");

        if (_sandboxSettings.Enabled)
            return Result.Success<UserRole?>(UserRole.Owner);

        if (board.OwnerId == userId)
            return Result.Success<UserRole?>(UserRole.Owner);

        var access = await _unitOfWork.BoardAccesses.GetByBoardAndUserAsync(boardId, userId);
        return Result.Success<UserRole?>(access?.Role);
    }
}
