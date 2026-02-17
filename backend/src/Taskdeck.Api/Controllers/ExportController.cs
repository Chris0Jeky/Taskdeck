using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public class ExportController : ControllerBase
{
    private readonly ExportImportService _exportImportService;

    public ExportController(ExportImportService exportImportService)
    {
        _exportImportService = exportImportService;
    }

    [HttpGet("export/boards/{boardId}")]
    public async Task<IActionResult> ExportBoard(Guid boardId, [FromQuery] Guid userId)
    {
        var result = await _exportImportService.ExportBoardAsync(boardId, userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("export/boards/{boardId}/json")]
    public async Task<IActionResult> ExportBoardAsJson(Guid boardId, [FromQuery] Guid userId)
    {
        var result = await _exportImportService.ExportBoardToJsonAsync(boardId, userId);
        return result.IsSuccess ? Content(result.Value, "application/json") : result.ToErrorActionResult();
    }

    [HttpPost("import/boards")]
    public async Task<IActionResult> ImportBoard([FromBody] ImportBoardDto dto, [FromQuery] Guid userId)
    {
        var result = await _exportImportService.ImportBoardAsync(dto, userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost("import/boards/json")]
    public async Task<IActionResult> ImportBoardFromJson([FromBody] JsonElement json, [FromQuery] Guid userId)
    {
        var result = await _exportImportService.ImportBoardFromJsonAsync(json.GetRawText(), userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
