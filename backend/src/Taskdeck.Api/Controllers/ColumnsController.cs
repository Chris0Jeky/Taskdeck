using Microsoft.AspNetCore.Mvc;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Route("api/boards/{boardId}/columns")]
public class ColumnsController : ControllerBase
{
    private readonly ColumnService _columnService;

    public ColumnsController(ColumnService columnService)
    {
        _columnService = columnService;
    }

    [HttpGet]
    public async Task<IActionResult> GetColumns(Guid boardId)
    {
        var result = await _columnService.GetColumnsByBoardIdAsync(boardId);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.ErrorMessage, statusCode: 500);
    }

    [HttpPost]
    public async Task<IActionResult> CreateColumn(Guid boardId, [FromBody] CreateColumnDto dto)
    {
        // Ensure boardId from route matches DTO
        var createDto = dto with { BoardId = boardId };
        var result = await _columnService.CreateColumnAsync(createDto);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return CreatedAtAction(nameof(GetColumns), new { boardId }, result.Value);
    }

    [HttpPatch("{columnId}")]
    public async Task<IActionResult> UpdateColumn(Guid boardId, Guid columnId, [FromBody] UpdateColumnDto dto)
    {
        var result = await _columnService.UpdateColumnAsync(columnId, dto);

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

    [HttpDelete("{columnId}")]
    public async Task<IActionResult> DeleteColumn(Guid boardId, Guid columnId)
    {
        var result = await _columnService.DeleteColumnAsync(columnId);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Conflict" => Conflict(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return NoContent();
    }
}
