using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BoardsController : ControllerBase
{
    private readonly BoardService _boardService;

    public BoardsController(BoardService boardService)
    {
        _boardService = boardService;
    }

    [HttpGet]
    public async Task<IActionResult> GetBoards([FromQuery] string? search, [FromQuery] bool includeArchived = false)
    {
        var result = await _boardService.ListBoardsAsync(search, includeArchived);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetBoard(Guid id)
    {
        var result = await _boardService.GetBoardDetailAsync(id);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> CreateBoard([FromBody] CreateBoardDto dto)
    {
        var result = await _boardService.CreateBoardAsync(dto);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetBoard), new { id = result.Value.Id }, result.Value)
            : result.ToErrorActionResult();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBoard(Guid id, [FromBody] UpdateBoardDto dto)
    {
        var result = await _boardService.UpdateBoardAsync(id, dto);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBoard(Guid id)
    {
        var result = await _boardService.DeleteBoardAsync(id);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }
}
