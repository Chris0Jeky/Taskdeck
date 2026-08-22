using Microsoft.Extensions.Logging;
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
    private readonly ILogger<BoardService>? _logger;

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
        _logger = logger;
    }

    private Task SafeLogAsync(string entityType, Guid entityId, AuditAction action, Guid? userId = null, string? changes = null)
        => AuditLogWriter.SafeLogAsync(_historyService, _logger, entityType, entityId, action, userId, changes);

    public async Task<Result<BoardDto>> CreateBoardAsync(CreateBoardDto dto, Guid actingUserId, CancellationToken cancellationToken = default)
    {
        if (actingUserId == Guid.Empty)
            return Result.Failure<BoardDto>(ErrorCodes.ValidationError, "Acting user ID cannot be empty");

        return await CreateBoardInternalAsync(dto, actingUserId, cancellationToken);
    }

    public async Task<Result<BoardDto>> UpdateBoardAsync(Guid id, UpdateBoardDto dto, CancellationToken cancellationToken = default)
    {
        // No acting user in hand — CanWrite fails closed (see BoardDto.CanWrite) and the audit
        // row is left unattributed rather than guessing an actor.
        return await UpdateBoardInternalAsync(id, dto, canWrite: false, actorUserId: null, cancellationToken);
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

        // The write check above just passed for this caller, so it is also the audit actor.
        return await UpdateBoardInternalAsync(id, dto, canWrite: true, actorUserId: actingUserId, cancellationToken);
    }

    public async Task<Result<BoardDetailDto>> GetBoardDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // NOTE: Board detail is intentionally NOT cached because BoardDetailDto includes
        // columns with card counts. ColumnService and CardService mutate this data without
        // cache awareness, so caching here would serve stale column/card information.
        // Board *list* caching is safe because BoardDto excludes columns and card counts.
        var board = await _unitOfWork.Boards.GetByIdWithDetailsAsync(id, cancellationToken);
        if (board == null)
            return Result.Failure<BoardDetailDto>(ErrorCodes.NotFound, $"Board with ID {id} not found");

        return Result.Success(MapToDetailDto(board));
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

    /// <summary>
    /// Lists boards without user-scoped authorization. Not cached because this overload
    /// has no user identity to scope the cache key, and caching an unscoped result could
    /// serve stale data when user-specific caches are invalidated independently.
    /// The user-scoped <see cref="ListBoardsAsync(Guid, string?, bool, CancellationToken)"/>
    /// overload IS cached.
    /// </summary>
    public async Task<Result<IEnumerable<BoardDto>>> ListBoardsAsync(string? searchText = null, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var boards = await _unitOfWork.Boards.SearchAsync(searchText, includeArchived, cancellationToken);
        // No acting user in hand — CanWrite fails closed (see BoardDto.CanWrite).
        return Result.Success(boards.Select(board => MapToDto(board, canWrite: false)));
    }

    public async Task<Result<IEnumerable<BoardDto>>> ListBoardsAsync(Guid actingUserId, string? searchText = null, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        // Delegate to paginated overload with no offset/limit to return all results.
        var paginated = await ListBoardsPaginatedAsync(actingUserId, searchText, includeArchived, offset: 0, limit: null, cancellationToken);
        if (!paginated.IsSuccess)
            return Result.Failure<IEnumerable<BoardDto>>(paginated.ErrorCode, paginated.ErrorMessage);

        return Result.Success<IEnumerable<BoardDto>>(paginated.Value.Items);
    }

    /// <summary>
    /// Lists boards with offset/limit pagination. When <paramref name="limit"/> is null,
    /// all authorized boards are returned (backward compatible behavior).
    /// </summary>
    public async Task<Result<PaginatedResult<BoardDto>>> ListBoardsPaginatedAsync(
        Guid actingUserId,
        string? searchText = null,
        bool includeArchived = false,
        int offset = 0,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (actingUserId == Guid.Empty)
            return Result.Failure<PaginatedResult<BoardDto>>(ErrorCodes.ValidationError, "Acting user ID cannot be empty");

        if (offset < 0)
            offset = 0;

        // Only cache un-filtered, non-archived, non-paginated list (the most common request)
        var canCache = _cacheService is not null && string.IsNullOrEmpty(searchText) && !includeArchived && offset == 0 && limit is null;

        if (canCache)
        {
            var cacheKey = CacheKeys.BoardListForUser(actingUserId);
            var cached = await _cacheService!.GetAsync<List<BoardDto>>(cacheKey, cancellationToken);
            if (cached is not null)
            {
                return Result.Success(new PaginatedResult<BoardDto>(cached, cached.Count, false, 0, cached.Count));
            }
        }

        var candidateBoardIds = (await _unitOfWork.Boards.SearchIdsAsync(searchText, includeArchived, cancellationToken)).ToList();

        List<Guid> visibleBoardIds;
        if (_authorizationService is null)
        {
            visibleBoardIds = candidateBoardIds;
        }
        else
        {
            var visibleBoardIdsResult = await _authorizationService.GetReadableBoardIdsAsync(
                actingUserId,
                candidateBoardIds,
                cancellationToken);

            if (!visibleBoardIdsResult.IsSuccess)
                return Result.Failure<PaginatedResult<BoardDto>>(visibleBoardIdsResult.ErrorCode, visibleBoardIdsResult.ErrorMessage);

            // GetReadableBoardIdsAsync returns IReadOnlySet<Guid> (unordered).
            // Preserve the stable order from candidateBoardIds (which is sorted by
            // CreatedAt desc, Id asc from the repository) by filtering rather than
            // converting the set to a list directly.
            var authorizedSet = visibleBoardIdsResult.Value;
            visibleBoardIds = candidateBoardIds.Where(id => authorizedSet.Contains(id)).ToList();
        }

        var totalCount = visibleBoardIds.Count;

        // Apply pagination to the stably-ordered authorized list of IDs
        IEnumerable<Guid> pageIds;
        if (limit.HasValue)
        {
            pageIds = visibleBoardIds.Skip(offset).Take(limit.Value);
        }
        else
        {
            pageIds = visibleBoardIds;
        }

        var pageIdList = pageIds.ToList();
        var boards = await _unitOfWork.Boards.GetByIdsAsync(pageIdList, cancellationToken);

        // Write capability for the CALLING user, stamped on each board so the client never has
        // to guess (and never has to ask per board). Exactly ONE batched lookup for the whole
        // page — calling CanWriteBoardAsync per board would be an N+1 of a board fetch plus a
        // membership read each. The owner short-circuit lives inside the batched lookup.
        IReadOnlySet<Guid>? writableBoardIds = null;
        if (_authorizationService is not null)
        {
            var writableResult = await _authorizationService.GetWritableBoardIdsAsync(
                actingUserId,
                pageIdList,
                cancellationToken);

            if (!writableResult.IsSuccess)
                return Result.Failure<PaginatedResult<BoardDto>>(writableResult.ErrorCode, writableResult.ErrorMessage);

            writableBoardIds = writableResult.Value;
        }

        // No authorization service configured (CLI / unauthenticated composition) means no
        // authorization is being enforced at all — the same convention EnsureBoardPermissionAsync
        // follows when it short-circuits to Success.
        var dtos = boards
            .Select(board => MapToDto(board, canWrite: writableBoardIds?.Contains(board.Id) ?? true))
            .ToList();

        var hasMore = limit.HasValue && (offset + pageIdList.Count) < totalCount;

        // Cache only the full un-paginated result
        if (canCache)
        {
            var ttl = TimeSpan.FromSeconds(_cacheSettings!.BoardListTtlSeconds);
            await _cacheService!.SetAsync(CacheKeys.BoardListForUser(actingUserId), dtos, ttl, cancellationToken);
        }

        return Result.Success(new PaginatedResult<BoardDto>(dtos, totalCount, hasMore, offset, limit ?? totalCount));
    }

    public async Task<Result> DeleteBoardAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // No acting user in hand — the audit row is left unattributed rather than guessing an actor.
        return await DeleteBoardInternalAsync(id, actorUserId: null, cancellationToken);
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

        // The delete check above just passed for this caller, so it is also the audit actor.
        return await DeleteBoardInternalAsync(id, actorUserId: actingUserId, cancellationToken);
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

            // The creator is the owner, and an owner can always write its own board.
            return Result.Success(MapToDto(board, canWrite: ownerId.HasValue));
        }
        catch (DomainException ex)
        {
            return Result.Failure<BoardDto>(ex.ErrorCode, ex.Message);
        }
    }

    private async Task<Result<BoardDto>> UpdateBoardInternalAsync(Guid id, UpdateBoardDto dto, bool canWrite, Guid? actorUserId, CancellationToken cancellationToken)
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
                await SafeLogAsync("board", board.Id, AuditAction.Archived, actorUserId, changeSummary);
            else if (dto.IsArchived == false)
                await SafeLogAsync("board", board.Id, AuditAction.Unarchived, actorUserId, changeSummary);
            else
                await SafeLogAsync("board", board.Id, AuditAction.Updated, actorUserId, changeSummary);

            // Invalidate board list cache for the owner
            if (board.OwnerId.HasValue)
                await InvalidateBoardListCacheAsync(board.OwnerId.Value, cancellationToken);

            return Result.Success(MapToDto(board, canWrite));
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

    private async Task<Result> DeleteBoardInternalAsync(Guid id, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var board = await _unitOfWork.Boards.GetByIdAsync(id, cancellationToken);
        if (board == null)
            return Result.Failure(ErrorCodes.NotFound, $"Board with ID {id} not found");

        board.Archive(); // Soft delete
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.NotifyBoardMutationAsync(
            new BoardRealtimeEvent(board.Id, "board", "archived", board.Id, DateTimeOffset.UtcNow),
            cancellationToken);
        await SafeLogAsync("board", board.Id, AuditAction.Archived, actorUserId, $"name={board.Name}");

        // Invalidate board list cache for the owner
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

    /// <param name="canWrite">
    /// Whether the caller this DTO is being produced for may write the board. Defaults to
    /// <c>false</c> so a mapping with no acting user in hand fails closed rather than
    /// advertising a capability it never checked.
    /// </param>
    private static BoardDto MapToDto(Board board, bool canWrite = false)
    {
        return new BoardDto(
            board.Id,
            board.Name,
            board.Description,
            board.IsArchived,
            board.CreatedAt,
            board.UpdatedAt,
            canWrite
        );
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
