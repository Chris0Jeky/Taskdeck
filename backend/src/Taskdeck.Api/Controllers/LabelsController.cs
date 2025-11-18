using Microsoft.AspNetCore.Mvc;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Route("api/boards/{boardId}/labels")]
public class LabelsController : ControllerBase
{
    private readonly LabelService _labelService;

    public LabelsController(LabelService labelService)
    {
        _labelService = labelService;
    }

    [HttpGet]
    public async Task<IActionResult> GetLabels(Guid boardId)
    {
        var result = await _labelService.GetLabelsByBoardIdAsync(boardId);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.ErrorMessage, statusCode: 500);
    }

    [HttpPost]
    public async Task<IActionResult> CreateLabel(Guid boardId, [FromBody] CreateLabelDto dto)
    {
        var createDto = dto with { BoardId = boardId };
        var result = await _labelService.CreateLabelAsync(createDto);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return CreatedAtAction(nameof(GetLabels), new { boardId }, result.Value);
    }

    [HttpPatch("{labelId}")]
    public async Task<IActionResult> UpdateLabel(Guid boardId, Guid labelId, [FromBody] UpdateLabelDto dto)
    {
        var result = await _labelService.UpdateLabelAsync(labelId, dto);

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

    [HttpDelete("{labelId}")]
    public async Task<IActionResult> DeleteLabel(Guid boardId, Guid labelId)
    {
        var result = await _labelService.DeleteLabelAsync(labelId);

        if (!result.IsSuccess)
        {
            return result.ErrorCode == "NotFound"
                ? NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
                : Problem(result.ErrorMessage, statusCode: 500);
        }

        return NoContent();
    }
}
