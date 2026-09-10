using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/import/boards/preview")]
public sealed class BoardImportPreviewController : AuthenticatedControllerBase
{
    private readonly IBoardJsonExportImportService imports;

    public BoardImportPreviewController(IBoardJsonExportImportService imports, IUserContext userContext) : base(userContext)
    {
        this.imports = imports;
    }

    [HttpPost]
    public async Task<IActionResult> Preview([FromBody] JsonElement json)
    {
        if (!TryGetCurrentUserId(out var actor, out var error)) return error!;
        var result = await imports.PreviewBoardAsync(json.GetRawText(), actor);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
