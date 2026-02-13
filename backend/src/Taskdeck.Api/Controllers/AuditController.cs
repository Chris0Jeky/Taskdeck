using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuditController : ControllerBase
{
    private readonly HistoryService _historyService;

    public AuditController(HistoryService historyService)
    {
        _historyService = historyService;
    }

    [HttpGet("boards/{boardId}")]
    public async Task<IActionResult> GetBoardHistory(Guid boardId, [FromQuery] int limit = 100)
    {
        var result = await _historyService.GetBoardHistoryAsync(boardId, limit);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("entities/{entityType}/{entityId}")]
    public async Task<IActionResult> GetEntityHistory(string entityType, Guid entityId, [FromQuery] int limit = 100)
    {
        var result = await _historyService.GetEntityHistoryAsync(entityType, entityId, limit);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("users/{userId}")]
    public async Task<IActionResult> GetUserHistory(Guid userId, [FromQuery] int limit = 100)
    {
        var result = await _historyService.GetUserHistoryAsync(userId, limit);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
