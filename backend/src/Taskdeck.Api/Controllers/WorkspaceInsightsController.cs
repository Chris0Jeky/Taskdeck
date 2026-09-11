using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workspace-insights")]
public class WorkspaceInsightsController : AuthenticatedControllerBase
{
    private readonly WorkspaceInsightService service;
    public WorkspaceInsightsController(WorkspaceInsightService service, IUserContext userContext) : base(userContext)
    { this.service = service; }
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid boardId, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var user, out var error)) return error!;
        var result = await service.ListAsync(user, boardId, false, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze(AnalyzeWorkspaceDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var user, out var error)) return error!;
        var result = await service.ListAsync(user, dto.BoardId, true, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    [HttpGet("observation-source")]
    public async Task<IActionResult> ObservationSource([FromQuery] Guid boardId, [FromQuery] Guid cardId,
        [FromServices] WorkspaceObservationService observations, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var user, out var error)) return error!;
        var result = await observations.SourceAsync(user, boardId, cardId, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    [HttpPost("model-analysis")]
    public async Task<IActionResult> ModelAnalysis(GenerateObservationsDto dto,
        [FromServices] WorkspaceObservationService observations, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var user, out var error)) return error!;
        var result = await observations.GenerateAsync(user, dto, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Act(Guid id, InsightActionDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var user, out var error)) return error!;
        var result = await service.ActAsync(user, id, dto.Action, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    [HttpPost("{id:guid}/answer")]
    public async Task<IActionResult> Answer(Guid id, AnswerInsightDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var user, out var error)) return error!;
        var result = await service.AnswerAsync(user, id, dto, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
