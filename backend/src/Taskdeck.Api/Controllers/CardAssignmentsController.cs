using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/boards/{boardId:guid}")]
public sealed class CardAssignmentsController : AuthenticatedControllerBase
{
    private readonly CardAssignmentService assignments;

    public CardAssignmentsController(CardAssignmentService assignments, IUserContext userContext) : base(userContext)
    {
        this.assignments = assignments;
    }

    [HttpGet("participants")]
    public async Task<IActionResult> Participants(Guid boardId, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var actor, out var error)) return error!;
        var result = await assignments.ParticipantsAsync(boardId, actor, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPut("cards/{cardId:guid}/assignments")]
    public async Task<IActionResult> Replace(Guid boardId, Guid cardId, ReplaceCardAssignmentsDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var actor, out var error)) return error!;
        var result = await assignments.ReplaceAsync(boardId, cardId, dto, actor, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
