using Microsoft.AspNetCore.Mvc;
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
        if (!result.IsSuccess)
            return MapError(result.ErrorCode, result.ErrorMessage);

        return Ok(result.Value);
    }

    [HttpGet("entities/{entityType}/{entityId}")]
    public async Task<IActionResult> GetEntityHistory(string entityType, Guid entityId, [FromQuery] int limit = 100)
    {
        var result = await _historyService.GetEntityHistoryAsync(entityType, entityId, limit);
        if (!result.IsSuccess)
            return MapError(result.ErrorCode, result.ErrorMessage);

        return Ok(result.Value);
    }

    [HttpGet("users/{userId}")]
    public async Task<IActionResult> GetUserHistory(Guid userId, [FromQuery] int limit = 100)
    {
        var result = await _historyService.GetUserHistoryAsync(userId, limit);
        if (!result.IsSuccess)
            return MapError(result.ErrorCode, result.ErrorMessage);

        return Ok(result.Value);
    }

    private IActionResult MapError(string errorCode, string message)
    {
        return errorCode switch
        {
            "NotFound" => NotFound(new { errorCode, message }),
            "ValidationError" => BadRequest(new { errorCode, message }),
            "AuthenticationFailed" => Unauthorized(new { errorCode, message }),
            "Forbidden" => StatusCode(403, new { errorCode, message }),
            "Conflict" => Conflict(new { errorCode, message }),
            _ => Problem(message, statusCode: 500)
        };
    }
}
