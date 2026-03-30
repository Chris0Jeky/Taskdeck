using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Manage boards for the authenticated user. Boards are the top-level container
/// for columns, cards, and labels.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
[Produces("application/json")]
public class BoardsController : AuthenticatedControllerBase
{
    private readonly BoardService _boardService;

    public BoardsController(BoardService boardService, IUserContext userContext) : base(userContext)
    {
        _boardService = boardService;
    }

    /// <summary>
    /// List boards accessible to the current user.
    /// </summary>
    /// <param name="search">Optional text filter applied to board names.</param>
    /// <param name="includeArchived">When true, archived boards are included in the results.</param>
    /// <returns>A list of boards matching the criteria.</returns>
    /// <response code="200">Returns the list of boards.</response>
    /// <response code="401">Authentication required.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<BoardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetBoards([FromQuery] string? search, [FromQuery] bool includeArchived = false)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _boardService.ListBoardsAsync(userId, search, includeArchived);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Get a board by ID, including its columns.
    /// </summary>
    /// <param name="id">The board identifier.</param>
    /// <returns>The board detail including columns.</returns>
    /// <response code="200">Returns the board detail.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Board not found or not accessible.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(BoardDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBoard(Guid id)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _boardService.GetBoardDetailAsync(id, userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Create a new board.
    /// </summary>
    /// <param name="dto">Board creation parameters.</param>
    /// <returns>The newly created board.</returns>
    /// <response code="201">Board created successfully.</response>
    /// <response code="400">Validation error in the request body.</response>
    /// <response code="401">Authentication required.</response>
    [HttpPost]
    [ProducesResponseType(typeof(BoardDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateBoard([FromBody] CreateBoardDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _boardService.CreateBoardAsync(dto, userId);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetBoard), new { id = result.Value.Id }, result.Value)
            : result.ToErrorActionResult();
    }

    /// <summary>
    /// Update an existing board.
    /// </summary>
    /// <param name="id">The board identifier.</param>
    /// <param name="dto">Fields to update (all optional).</param>
    /// <returns>The updated board.</returns>
    /// <response code="200">Board updated successfully.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Board not found or not accessible.</response>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(BoardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateBoard(Guid id, [FromBody] UpdateBoardDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _boardService.UpdateBoardAsync(id, dto, userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Delete a board.
    /// </summary>
    /// <param name="id">The board identifier.</param>
    /// <response code="204">Board deleted successfully.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Board not found or not accessible.</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBoard(Guid id)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _boardService.DeleteBoardAsync(id, userId);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }
}
