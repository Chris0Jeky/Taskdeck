using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
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
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> GrantAccess(Guid boardId, [FromBody] GrantAccessDto dto, [FromQuery] Guid grantedBy)
    {
        var dtoWithBoardId = dto with { BoardId = boardId };
        var result = await _boardAccessService.GrantAccessAsync(dtoWithBoardId, grantedBy);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPut("{accessId}")]
    public async Task<IActionResult> UpdateAccess(Guid boardId, Guid accessId, [FromBody] UpdateAccessDto dto, [FromQuery] Guid updatedBy)
    {
        var result = await _boardAccessService.UpdateAccessAsync(boardId, accessId, dto, updatedBy);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpDelete("{accessId}")]
    public async Task<IActionResult> RevokeAccess(Guid boardId, Guid accessId, [FromQuery] Guid revokedBy)
    {
        var result = await _boardAccessService.RevokeAccessAsync(boardId, accessId, revokedBy);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }
}
