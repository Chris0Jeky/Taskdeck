using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using BoardAuthorizationService = Taskdeck.Application.Services.IAuthorizationService;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Manage columns within a board. Columns organize cards into workflow stages
/// and support optional WIP (work-in-progress) limits.
/// </summary>
[ApiController]
[Authorize]
[Route("api/boards/{boardId}/columns")]
[Produces("application/json")]
public class ColumnsController : AuthenticatedControllerBase
{
    private readonly ColumnService _columnService;
    private readonly BoardAuthorizationService _authorizationService;

    public ColumnsController(
        ColumnService columnService,
        BoardAuthorizationService authorizationService,
        IUserContext userContext)
        : base(userContext)
    {
        _columnService = columnService;
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// List all columns for a board, ordered by position.
    /// </summary>
    /// <param name="boardId">The board identifier.</param>
    /// <returns>An ordered list of columns.</returns>
    /// <response code="200">Returns the columns for the board.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">User does not have read access to this board.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ColumnDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetColumns(Guid boardId)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanReadBoardAsync(actorId, targetBoardId),
            "You do not have access to this board");

        if (permissionError is not null)
            return permissionError;

        var result = await _columnService.GetColumnsByBoardIdAsync(boardId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Create a new column on a board.
    /// </summary>
    /// <param name="boardId">The board identifier.</param>
    /// <param name="dto">Column creation parameters including name and optional WIP limit.</param>
    /// <returns>The newly created column.</returns>
    /// <response code="201">Column created successfully.</response>
    /// <response code="400">Validation error.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">User does not have write access to this board.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ColumnDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateColumn(Guid boardId, [FromBody] CreateColumnDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanWriteBoardAsync(actorId, targetBoardId),
            "You do not have permission to modify this board");

        if (permissionError is not null)
            return permissionError;

        // Ensure boardId from route matches DTO
        var createDto = dto with { BoardId = boardId };
        var result = await _columnService.CreateColumnAsync(createDto);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetColumns), new { boardId }, result.Value)
            : result.ToErrorActionResult();
    }

    /// <summary>
    /// Update a column's name, position, or WIP limit.
    /// </summary>
    /// <param name="boardId">The board identifier.</param>
    /// <param name="columnId">The column identifier.</param>
    /// <param name="dto">Fields to update (all optional).</param>
    /// <returns>The updated column.</returns>
    /// <response code="200">Column updated successfully.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">User does not have write access to this board.</response>
    /// <response code="404">Column not found.</response>
    [HttpPatch("{columnId}")]
    [ProducesResponseType(typeof(ColumnDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateColumn(Guid boardId, Guid columnId, [FromBody] UpdateColumnDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanWriteBoardAsync(actorId, targetBoardId),
            "You do not have permission to modify this board");

        if (permissionError is not null)
            return permissionError;

        var result = await _columnService.UpdateColumnAsync(boardId, columnId, dto);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Delete a column from a board. The column must be empty (no cards).
    /// </summary>
    /// <param name="boardId">The board identifier.</param>
    /// <param name="columnId">The column identifier.</param>
    /// <response code="204">Column deleted successfully.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">User does not have write access to this board.</response>
    /// <response code="404">Column not found.</response>
    [HttpDelete("{columnId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteColumn(Guid boardId, Guid columnId)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanWriteBoardAsync(actorId, targetBoardId),
            "You do not have permission to modify this board");

        if (permissionError is not null)
            return permissionError;

        var result = await _columnService.DeleteColumnAsync(boardId, columnId);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }

    /// <summary>
    /// Reorder columns by providing the full list of column IDs in the desired order.
    /// </summary>
    /// <param name="boardId">The board identifier.</param>
    /// <param name="dto">The ordered list of column IDs.</param>
    /// <returns>The reordered columns.</returns>
    /// <response code="200">Columns reordered successfully.</response>
    /// <response code="400">Validation error (e.g., missing or extra column IDs).</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">User does not have write access to this board.</response>
    [HttpPost("reorder")]
    [ProducesResponseType(typeof(IEnumerable<ColumnDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ReorderColumns(Guid boardId, [FromBody] ReorderColumnsDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanWriteBoardAsync(actorId, targetBoardId),
            "You do not have permission to modify this board");

        if (permissionError is not null)
            return permissionError;

        var result = await _columnService.ReorderColumnsAsync(boardId, dto);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
