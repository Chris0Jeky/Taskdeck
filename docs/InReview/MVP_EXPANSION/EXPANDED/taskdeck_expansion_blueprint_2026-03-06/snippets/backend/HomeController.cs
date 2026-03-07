using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workspace")]
public sealed class WorkspaceController : AuthenticatedControllerBase
{
    private readonly IWorkspaceSummaryService _workspaceSummaryService;

    public WorkspaceController(IWorkspaceSummaryService workspaceSummaryService, IUserContext userContext)
        : base(userContext)
    {
        _workspaceSummaryService = workspaceSummaryService;
    }

    [HttpGet("home")]
    public async Task<IActionResult> GetHome(CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _workspaceSummaryService.GetHomeAsync(userId, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("today")]
    public async Task<IActionResult> GetToday(CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _workspaceSummaryService.GetTodayAsync(userId, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
