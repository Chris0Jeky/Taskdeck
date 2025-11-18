using Microsoft.AspNetCore.Mvc;
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
        return result.IsSuccess ? Ok(result.Value) : Problem(result.ErrorMessage, statusCode: 500);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetBoard(Guid id)
    {
        var result = await _boardService.GetBoardDetailAsync(id);

        if (!result.IsSuccess)
        {
            return result.ErrorCode == "NotFound"
                ? NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
                : Problem(result.ErrorMessage, statusCode: 500);
        }

        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<IActionResult> CreateBoard([FromBody] CreateBoardDto dto)
    {
        var result = await _boardService.CreateBoardAsync(dto);

        if (!result.IsSuccess)
        {
            return result.ErrorCode == "ValidationError"
                ? BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
                : Problem(result.ErrorMessage, statusCode: 500);
        }

        return CreatedAtAction(nameof(GetBoard), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBoard(Guid id, [FromBody] UpdateBoardDto dto)
    {
        var result = await _boardService.UpdateBoardAsync(id, dto);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return Ok(result.Value);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBoard(Guid id)
    {
        var result = await _boardService.DeleteBoardAsync(id);

        if (!result.IsSuccess)
        {
            return result.ErrorCode == "NotFound"
                ? NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
                : Problem(result.ErrorMessage, statusCode: 500);
        }

        return NoContent();
    }
}
