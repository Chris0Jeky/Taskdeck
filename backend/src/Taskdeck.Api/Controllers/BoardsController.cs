using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class BoardsController : AuthenticatedControllerBase
{
    private readonly BoardService _boardService;

    public BoardsController(BoardService boardService, IUserContext userContext) : base(userContext)
    {
        _boardService = boardService;
    }

    [HttpGet]
    public async Task<IActionResult> GetBoards([FromQuery] string? search, [FromQuery] bool includeArchived = false)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _boardService.ListBoardsAsync(userId, search, includeArchived);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetBoard(Guid id)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _boardService.GetBoardDetailAsync(id, userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> CreateBoard([FromBody] CreateBoardDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _boardService.CreateBoardAsync(dto, userId);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetBoard), new { id = result.Value.Id }, result.Value)
            : result.ToErrorActionResult();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBoard(Guid id, [FromBody] UpdateBoardDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _boardService.UpdateBoardAsync(id, dto, userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBoard(Guid id)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _boardService.DeleteBoardAsync(id, userId);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }
}
