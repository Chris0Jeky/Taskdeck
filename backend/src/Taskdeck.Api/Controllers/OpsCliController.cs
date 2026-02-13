using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/ops/cli")]
public class OpsCliController : AuthenticatedControllerBase
{
    private readonly IOpsCliService _opsCliService;

    public OpsCliController(IOpsCliService opsCliService, IUserContext userContext) : base(userContext)
    {
        _opsCliService = opsCliService;
    }

    [HttpPost("run")]
    public async Task<IActionResult> RunCommand([FromBody] RunCommandDto dto, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _opsCliService.RunCommandAsync(userId, dto, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("runs/{id}")]
    public async Task<IActionResult> GetCommandRun(Guid id, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _opsCliService.GetCommandRunAsync(id, ct);

        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        if (result.Value.RequestedByUserId != userId)
        {
            return StatusCode(403, new
            {
                errorCode = ErrorCodes.Forbidden,
                message = "You do not have access to this command run"
            });
        }

        return Ok(result.Value);
    }

    [HttpGet("runs/{id}/logs")]
    public async Task<IActionResult> GetCommandRunLogs(Guid id, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var runResult = await _opsCliService.GetCommandRunAsync(id, ct);

        if (!runResult.IsSuccess)
            return runResult.ToErrorActionResult();

        if (runResult.Value.RequestedByUserId != userId)
        {
            return StatusCode(403, new
            {
                errorCode = ErrorCodes.Forbidden,
                message = "You do not have access to this command run"
            });
        }

        var result = await _opsCliService.GetCommandRunLogsAsync(id, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("templates")]
    public IActionResult GetTemplates()
    {
        var result = _opsCliService.GetAvailableTemplates();
        return Ok(result.Value);
    }
}
