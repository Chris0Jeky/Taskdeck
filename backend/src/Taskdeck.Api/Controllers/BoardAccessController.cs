using Microsoft.AspNetCore.Mvc;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Route("api/boards/{boardId}/access")]
public class BoardAccessController : ControllerBase
{
    private readonly BoardAccessService _boardAccessService;

    public BoardAccessController(BoardAccessService boardAccessService)
    {
        _boardAccessService = boardAccessService;
    }

    [HttpGet]
    public async Task<IActionResult> GetBoardAccess(Guid boardId)
    {
        var result = await _boardAccessService.GetBoardAccessListAsync(boardId);

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

    [HttpPost]
    public async Task<IActionResult> GrantAccess(Guid boardId, [FromBody] GrantAccessDto dto, [FromQuery] Guid grantedBy)
    {
        var dtoWithBoardId = dto with { BoardId = boardId };
        var result = await _boardAccessService.GrantAccessAsync(dtoWithBoardId, grantedBy);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "AuthenticationFailed" => Unauthorized(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Forbidden" => StatusCode(403, new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Conflict" => Conflict(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return Ok(result.Value);
    }

    [HttpPut("{accessId}")]
    public async Task<IActionResult> UpdateAccess(Guid boardId, Guid accessId, [FromBody] UpdateAccessDto dto, [FromQuery] Guid updatedBy)
    {
        var result = await _boardAccessService.UpdateAccessAsync(boardId, accessId, dto, updatedBy);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "AuthenticationFailed" => Unauthorized(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Forbidden" => StatusCode(403, new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Conflict" => Conflict(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return Ok(result.Value);
    }

    [HttpDelete("{accessId}")]
    public async Task<IActionResult> RevokeAccess(Guid boardId, Guid accessId, [FromQuery] Guid revokedBy)
    {
        var result = await _boardAccessService.RevokeAccessAsync(boardId, accessId, revokedBy);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "AuthenticationFailed" => Unauthorized(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Forbidden" => StatusCode(403, new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Conflict" => Conflict(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return NoContent();
    }
}
