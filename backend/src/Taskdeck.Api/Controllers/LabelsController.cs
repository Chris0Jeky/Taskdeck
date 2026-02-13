using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
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
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> CreateLabel(Guid boardId, [FromBody] CreateLabelDto dto)
    {
        var createDto = dto with { BoardId = boardId };
        var result = await _labelService.CreateLabelAsync(createDto);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetLabels), new { boardId }, result.Value)
            : result.ToErrorActionResult();
    }

    [HttpPatch("{labelId}")]
    public async Task<IActionResult> UpdateLabel(Guid boardId, Guid labelId, [FromBody] UpdateLabelDto dto)
    {
        var result = await _labelService.UpdateLabelAsync(boardId, labelId, dto);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpDelete("{labelId}")]
    public async Task<IActionResult> DeleteLabel(Guid boardId, Guid labelId)
    {
        var result = await _labelService.DeleteLabelAsync(boardId, labelId);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }
}
