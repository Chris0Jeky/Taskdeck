using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class BoardService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthorizationService? _authorizationService;
    private readonly IBoardRealtimeNotifier _realtimeNotifier;
    private readonly IHistoryService? _historyService;
    private readonly ICacheService? _cacheService;
    private readonly CacheSettings? _cacheSettings;
    private readonly ILogger<BoardService> _logger;

    public BoardService(IUnitOfWork unitOfWork)
        : this(unitOfWork, authorizationService: null, realtimeNotifier: null, historyService: null)
    {
    }

    public BoardService(
        IUnitOfWork unitOfWork,
        IAuthorizationService? authorizationService,
        IBoardRealtimeNotifier? realtimeNotifier = null,
        IHistoryService? historyService = null,
        ICacheService? cacheService = null,
        CacheSettings? cacheSettings = null,
        ILogger<BoardService>? logger = null)
    {
        _unitOfWork = unitOfWork;
        _authorizationService = authorizationService;
        _realtimeNotifier = realtimeNotifier ?? NoOpBoardRealtimeNotifier.Instance;
        _historyService = historyService;
        _cacheService = cacheService;
        _cacheSettings = cacheSettings ?? new CacheSettings();
        _logger = logger ?? NullLogger<BoardService>.Instance;
    }

    private async Task SafeLogAsync(string entityType, Guid entityId, AuditAction action, Guid? userId = null, string? changes = null)
    {
        if (_historyService == null) return;
        try { await _historyService.LogActionAsync(entityType, entityId, action, userId, changes); }
        catch (Exception) { /* Audit is secondary — never crash the mutation */ }
    }

    public async Task<Result<BoardDto>> CreateBoardAsync(CreateBoardDto dto, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        if (actingUserId == Guid.Empty)
            return Result.Failure<BoardDto>(ErrorCodes.ValidationError, "Acting user ID cannot be empty");

        return await CreateBoardInternalAsync(dto, actingUserId, cancellationToken);
    }

    public async Task<Result<BoardDto>> UpdateBoardAsync(Guid id, UpdateBoardDto dto, CancellationToken cancellationToken = default)
    {
        return await UpdateBoardInternalAsync(id, dto, cancellationToken);
    }

    public async Task<Result<BoardDto>> UpdateBoardAsync(Guid id, UpdateBoardDto dto, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var permission = await EnsureBoardPermissionAsync(
            actingUserId,
            id,
            static (authorizationService, userId, boardId) => authorizationService.CanWriteBoardAsync(userId, boardId),
            "You do not have permission to update this board");

        if (!permission.IsSuccess)
            return Result.Failure<BoardDto>(permission.ErrorCode, permission.ErrorMessage);

        return await UpdateBoardInternalAsync(id, dto, cancellationToken);
    }

    public async Task<Result<BoardDetailDto>> GetBoardDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Try cache first
        if (_cacheService is not null)
        {
            var cacheKey = CacheKeys.BoardDetail(id);
            var cached = await _cacheService.GetAsync<BoardDetailDto>(cacheKey, cancellationToken);
            if (cached is not null)
            {
                return Result.Success(cached);
            }
        }

        var board = await _unitOfWork.Boards.GetByIdWithDetailsAsync(id, cancellationToken);
        if (board == null)
            return Result.Failure<BoardDetailDto>(ErrorCodes.NotFound, $"Board with ID {id} not found");

        var dto = MapToDetailDto(board);

        // Populate cache
        if (_cacheService is not null)
        {
            var ttl = TimeSpan.FromSeconds(_cacheSettings!.BoardDetailTtlSeconds);
            await _cacheService.SetAsync(CacheKeys.BoardDetail(id), dto, ttl, cancellationToken);
        }

        return Result.Success(dto);
    }

    public async Task<Result<BoardDetailDto>> GetBoardDetailAsync(Guid id, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var permission = await EnsureBoardPermissionAsync(
            actingUserId,
            id,
            static (authorizationService, userId, boardId) => authorizationService.CanReadBoardAsync(userId, boardId),
            "You do not have access to this board");

        if (!permission.IsSuccess)
            return Result.Failure<BoardDetailDto>(permission.ErrorCode, permission.ErrorMessage);

        return await GetBoardDetailAsync(id, cancellationToken);
    }

    public async Task<Result<IEnumerable<BoardDto>>> ListBoardsAsync(string? searchText = null, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var boards = await _unitOfWork.Boards.SearchAsync(searchText, includeArchived, cancellationToken);
        return Result.Success(boards.Select(MapToDto));
    }

    public async Task<Result<IEnumerable<BoardDto>>> ListBoardsAsync(Guid actingUserId, string? searchText = null, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        if (actingUserId == Guid.Empty)
            return Result.Failure<IEnumerable<BoardDto>>(ErrorCodes.ValidationError, "Acting user ID cannot be empty");

        // Only cache un-filtered, non-archived list (the most common request)
        var canCache = _cacheService is not null && string.IsNullOrEmpty(searchText) && !includeArchived;

        if (canCache)
        {
            var cacheKey = CacheKeys.BoardListForUser(actingUserId);
            var cached = await _cacheService!.GetAsync<List<BoardDto>>(cacheKey, cancellationToken);
            if (cached is not null)
            {
                return Result.Success<IEnumerable<BoardDto>>(cached);
            }
        }

        var candidateBoardIds = (await _unitOfWork.Boards.SearchIdsAsync(searchText, includeArchived, cancellationToken)).ToList();

        if (_authorizationService is null)
        {
            var boards = await _unitOfWork.Boards.GetByIdsAsync(candidateBoardIds, cancellationToken);
            var dtos = boards.Select(MapToDto).ToList();

            if (canCache)
            {
                var ttl = TimeSpan.FromSeconds(_cacheSettings!.BoardListTtlSeconds);
                await _cacheService!.SetAsync(CacheKeys.BoardListForUser(actingUserId), dtos, ttl, cancellationToken);
            }

            return Result.Success<IEnumerable<BoardDto>>(dtos);
        }

        var visibleBoardIdsResult = await _authorizationService.GetReadableBoardIdsAsync(
            actingUserId,
            candidateBoardIds,
            cancellationToken);

        if (!visibleBoardIdsResult.IsSuccess)
            return Result.Failure<IEnumerable<BoardDto>>(visibleBoardIdsResult.ErrorCode, visibleBoardIdsResult.ErrorMessage);

        var visibleBoardIds = visibleBoardIdsResult.Value.ToList();
        var visibleBoards = await _unitOfWork.Boards.GetByIdsAsync(visibleBoardIds, cancellationToken);
        var result = visibleBoards.Select(MapToDto).ToList();

        if (canCache)
        {
            var ttl = TimeSpan.FromSeconds(_cacheSettings!.BoardListTtlSeconds);
            await _cacheService!.SetAsync(CacheKeys.BoardListForUser(actingUserId), result, ttl, cancellationToken);
        }

        return Result.Success<IEnumerable<BoardDto>>(result);
    }

    public async Task<Result> DeleteBoardAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await DeleteBoardInternalAsync(id, cancellationToken);
    }

    public async Task<Result> DeleteBoardAsync(Guid id, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        var permission = await EnsureBoardPermissionAsync(
            actingUserId,
            id,
            static (authorizationService, userId, boardId) => authorizationService.CanDeleteBoardAsync(userId, boardId),
            "You do not have permission to delete this board");

        if (!permission.IsSuccess)
            return permission;

        return await DeleteBoardInternalAsync(id, cancellationToken);
    }

    private async Task<Result<BoardDto>> CreateBoardInternalAsync(CreateBoardDto dto, Guid? ownerId, CancellationToken cancellationToken)
    {
        try
        {
            var board = new Board(dto.Name, dto.Description, ownerId);
            await _unitOfWork.Boards.AddAsync(board, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _realtimeNotifier.NotifyBoardMutationAsync(
                new BoardRealtimeEvent(board.Id, "board", "created", board.Id, DateTimeOffset.UtcNow),
                cancellationToken);
            await SafeLogAsync("board", board.Id, AuditAction.Created, ownerId, $"name={board.Name}");

            // Invalidate board list cache for the owner
            if (ownerId.HasValue)
                await InvalidateBoardListCacheAsync(ownerId.Value, cancellationToken);

            return Result.Success(MapToDto(board));
        }
        catch (DomainException ex)
        {
            return Result.Failure<BoardDto>(ex.ErrorCode, ex.Message);
        }
    }

    private async Task<Result<BoardDto>> UpdateBoardInternalAsync(Guid id, UpdateBoardDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var board = await _unitOfWork.Boards.GetByIdAsync(id, cancellationToken);
            if (board == null)
                return Result.Failure<BoardDto>(ErrorCodes.NotFound, $"Board with ID {id} not found");

            // Capture pre-mutation state for change summary
            var oldName = board.Name;
            var oldDescription = board.Description;
            var oldIsArchived = board.IsArchived;

            if (dto.Name != null || dto.Description != null)
                board.Update(dto.Name, dto.Description);

            if (dto.IsArchived.HasValue)
            {
                if (dto.IsArchived.Value)
                    board.Archive();
                else
                    board.Unarchive();
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _realtimeNotifier.NotifyBoardMutationAsync(
                new BoardRealtimeEvent(board.Id, "board", "updated", board.Id, DateTimeOffset.UtcNow),
                cancellationToken);

            var changeSummary = BuildBoardChangeSummary(dto, oldName, oldDescription, oldIsArchived);

            if (dto.IsArchived == true)
                await SafeLogAsync("board", board.Id, AuditAction.Archived, changes: changeSummary);
            else if (dto.IsArchived == false)
                await SafeLogAsync("board", board.Id, AuditAction.Unarchived, changes: changeSummary);
            else
                await SafeLogAsync("board", board.Id, AuditAction.Updated, changes: changeSummary);

            // Invalidate board detail cache and board list caches
            await InvalidateBoardDetailCacheAsync(board.Id, cancellationToken);
            if (board.OwnerId.HasValue)
                await InvalidateBoardListCacheAsync(board.OwnerId.Value, cancellationToken);

            return Result.Success(MapToDto(board));
        }
        catch (DomainException ex)
        {
            return Result.Failure<BoardDto>(ex.ErrorCode, ex.Message);
        }
    }

    private static string BuildBoardChangeSummary(UpdateBoardDto dto, string oldName, string? oldDescription, bool oldIsArchived)
    {
        var parts = new List<string>();
        if (dto.Name != null && dto.Name != oldName)
            parts.Add($"Name: '{oldName}' -> '{dto.Name}'");
        if (dto.Description != null && dto.Description != oldDescription)
            parts.Add($"Description changed");
        if (dto.IsArchived.HasValue && dto.IsArchived.Value != oldIsArchived)
            parts.Add(dto.IsArchived.Value ? "Archived" : "Unarchived");
        return parts.Count > 0 ? string.Join("; ", parts) : "no fields changed";
    }

    public async Task<Result<BoardDto>> GetBoardByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var board = await _unitOfWork.Boards.GetByIdAsync(id, cancellationToken);
        if (board == null)
            return Result.Failure<BoardDto>(ErrorCodes.NotFound, $"Board with ID {id} not found");

        return Result.Success(MapToDto(board));
    }

    private async Task<Result> DeleteBoardInternalAsync(Guid id, CancellationToken cancellationToken)
    {
        var board = await _unitOfWork.Boards.GetByIdAsync(id, cancellationToken);
        if (board == null)
            return Result.Failure(ErrorCodes.NotFound, $"Board with ID {id} not found");

        board.Archive(); // Soft delete
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.NotifyBoardMutationAsync(
            new BoardRealtimeEvent(board.Id, "board", "archived", board.Id, DateTimeOffset.UtcNow),
            cancellationToken);
        await SafeLogAsync("board", board.Id, AuditAction.Archived, changes: $"name={board.Name}");

        // Invalidate caches
        await InvalidateBoardDetailCacheAsync(board.Id, cancellationToken);
        if (board.OwnerId.HasValue)
            await InvalidateBoardListCacheAsync(board.OwnerId.Value, cancellationToken);

        return Result.Success();
    }

    private async Task<Result> EnsureBoardPermissionAsync(
        Guid actingUserId,
        Guid boardId,
        Func<IAuthorizationService, Guid, Guid, Task<Result<bool>>> permissionCheck,
        string forbiddenMessage)
    {
        if (actingUserId == Guid.Empty)
            return Result.Failure(ErrorCodes.ValidationError, "Acting user ID cannot be empty");

        if (_authorizationService is null)
            return Result.Success();

        var permission = await permissionCheck(_authorizationService, actingUserId, boardId);
        if (!permission.IsSuccess)
            return Result.Failure(permission.ErrorCode, permission.ErrorMessage);

        return permission.Value
            ? Result.Success()
            : Result.Failure(ErrorCodes.Forbidden, forbiddenMessage);
    }

    private static BoardDto MapToDto(Board board)
    {
        return new BoardDto(
            board.Id,
            board.Name,
            board.Description,
            board.IsArchived,
            board.CreatedAt,
            board.UpdatedAt
        );
    }

    private async Task InvalidateBoardDetailCacheAsync(Guid boardId, CancellationToken cancellationToken)
    {
        if (_cacheService is null) return;
        await _cacheService.RemoveAsync(CacheKeys.BoardDetail(boardId), cancellationToken);
    }

    private async Task InvalidateBoardListCacheAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_cacheService is null) return;
        await _cacheService.RemoveAsync(CacheKeys.BoardListForUser(userId), cancellationToken);
    }

    private BoardDetailDto MapToDetailDto(Board board)
    {
        var columns = board.Columns
            .OrderBy(c => c.Position)
            .Select(c => new ColumnDto(
                c.Id,
                c.BoardId,
                c.Name,
                c.Position,
                c.WipLimit,
                c.Cards.Count,
                c.CreatedAt,
                c.UpdatedAt
            ))
            .ToList();

        return new BoardDetailDto(
            board.Id,
            board.Name,
            board.Description,
            board.IsArchived,
            board.CreatedAt,
            board.UpdatedAt,
            columns
        );
    }
}
