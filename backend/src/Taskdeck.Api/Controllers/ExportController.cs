using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public class ExportController : AuthenticatedControllerBase
{
    private readonly IExportImportService _exportImportService;
    private readonly DatabaseExportImportSettings _databaseSettings;

    public ExportController(
        IExportImportService exportImportService,
        DatabaseExportImportSettings databaseSettings,
        IUserContext userContext)
        : base(userContext)
    {
        _exportImportService = exportImportService;
        _databaseSettings = databaseSettings;
    }

    [HttpGet("export/boards/{boardId}")]
    public async Task<IActionResult> ExportBoard(Guid boardId)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _exportImportService.ExportBoardAsync(boardId, userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("export/boards/{boardId}/json")]
    public async Task<IActionResult> ExportBoardAsJson(Guid boardId)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _exportImportService.ExportBoardToJsonAsync(boardId, userId);
        return result.IsSuccess ? Content(result.Value, "application/json") : result.ToErrorActionResult();
    }

    [HttpPost("import/boards")]
    public async Task<IActionResult> ImportBoard([FromBody] ImportBoardDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _exportImportService.ImportBoardAsync(dto, userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost("import/boards/json")]
    public async Task<IActionResult> ImportBoardFromJson([FromBody] JsonElement json)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _exportImportService.ImportBoardFromJsonAsync(json.GetRawText(), userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("export/database")]
    public async Task<IActionResult> ExportDatabase()
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _exportImportService.ExportDatabaseAsync(userId);
        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        var fileName = $"taskdeck-db-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.db";
        return File(result.Value, "application/octet-stream", fileName);
    }

    [HttpPost("import/database")]
    public async Task<IActionResult> ImportDatabase([FromForm] IFormFile? file)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        if (file is null)
        {
            return Result
                .Failure(ErrorCodes.ValidationError, "Database import requires a file upload")
                .ToErrorActionResult();
        }

        if (file.Length == 0)
        {
            return Result
                .Failure(ErrorCodes.ValidationError, "Database import payload cannot be empty")
                .ToErrorActionResult();
        }

        var maxImportBytes = Math.Clamp(
            _databaseSettings.MaxImportBytes,
            1 * 1024 * 1024,
            500 * 1024 * 1024);
        if (file.Length > maxImportBytes)
        {
            return Result
                .Failure(
                    ErrorCodes.ValidationError,
                    $"Database import payload exceeds max size of {maxImportBytes} bytes")
                .ToErrorActionResult();
        }

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            bytes = memoryStream.ToArray();
        }

        var result = await _exportImportService.ImportDatabaseAsync(bytes, userId);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }
}
