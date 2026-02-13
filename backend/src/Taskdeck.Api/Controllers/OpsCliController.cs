using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/ops/cli")]
public class OpsCliController : ControllerBase
{
    private readonly IOpsCliService _opsCliService;
    private readonly IUserContext _userContext;

    public OpsCliController(IOpsCliService opsCliService, IUserContext userContext)
    {
        _opsCliService = opsCliService;
        _userContext = userContext;
    }

    [HttpPost("run")]
    public async Task<IActionResult> RunCommand([FromBody] RunCommandDto dto, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _opsCliService.RunCommandAsync(userId, dto, ct);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Forbidden" => StatusCode(403, new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return Ok(result.Value);
    }

    [HttpGet("runs/{id}")]
    public async Task<IActionResult> GetCommandRun(Guid id, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _opsCliService.GetCommandRunAsync(id, ct);

        if (!result.IsSuccess)
        {
            return result.ErrorCode == "NotFound"
                ? NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
                : Problem(result.ErrorMessage, statusCode: 500);
        }
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
        {
            return runResult.ErrorCode == "NotFound"
                ? NotFound(new { errorCode = runResult.ErrorCode, message = runResult.ErrorMessage })
                : Problem(runResult.ErrorMessage, statusCode: 500);
        }
        if (runResult.Value.RequestedByUserId != userId)
        {
            return StatusCode(403, new
            {
                errorCode = ErrorCodes.Forbidden,
                message = "You do not have access to this command run"
            });
        }

        var result = await _opsCliService.GetCommandRunLogsAsync(id, ct);

        if (!result.IsSuccess)
        {
            return result.ErrorCode == "NotFound"
                ? NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
                : Problem(result.ErrorMessage, statusCode: 500);
        }

        return Ok(result.Value);
    }

    [HttpGet("templates")]
    public IActionResult GetTemplates()
    {
        var result = _opsCliService.GetAvailableTemplates();
        return Ok(result.Value);
    }

    private bool TryGetCurrentUserId(out Guid userId, out IActionResult? errorResult)
    {
        userId = Guid.Empty;
        errorResult = null;

        if (!_userContext.IsAuthenticated || string.IsNullOrWhiteSpace(_userContext.UserId))
        {
            errorResult = Unauthorized(new
            {
                errorCode = ErrorCodes.AuthenticationFailed,
                message = "Authenticated user context is required"
            });
            return false;
        }

        if (!Guid.TryParse(_userContext.UserId, out userId))
        {
            errorResult = Unauthorized(new
            {
                errorCode = ErrorCodes.AuthenticationFailed,
                message = "Authenticated user id claim is invalid"
            });
            return false;
        }

        return true;
    }
}
