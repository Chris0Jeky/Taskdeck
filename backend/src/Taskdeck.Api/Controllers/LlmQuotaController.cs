using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/llm")]
public class LlmQuotaController : AuthenticatedControllerBase
{
    private readonly ILlmQuotaService _quotaService;
    private readonly ILlmKillSwitchService _killSwitchService;

    public LlmQuotaController(
        ILlmQuotaService quotaService,
        ILlmKillSwitchService killSwitchService,
        IUserContext userContext) : base(userContext)
    {
        _quotaService = quotaService;
        _killSwitchService = killSwitchService;
    }

    [HttpGet("quota/usage")]
    public async Task<IActionResult> GetUsageSummary(
        [FromQuery] Guid? userId = null,
        [FromQuery] LlmSurface? surface = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out _, out var errorResult))
            return errorResult!;

        var summary = await _quotaService.GetUsageSummaryAsync(userId, surface, from, to, ct);
        return Ok(summary);
    }

    [HttpGet("quota/status")]
    public async Task<IActionResult> GetQuotaStatus(CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var status = await _quotaService.GetQuotaStatusAsync(userId, ct);
        return Ok(status);
    }

    [HttpPost("killswitch")]
    public async Task<IActionResult> SetKillSwitch(
        [FromBody] SetKillSwitchRequestDto dto,
        CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out _, out var errorResult))
            return errorResult!;

        var result = await _killSwitchService.SetKillSwitchAsync(
            dto.Scope, dto.Target, dto.Enabled, dto.Reason, ct);

        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        return Ok(await _killSwitchService.GetStatusAsync(ct));
    }

    [HttpGet("killswitch")]
    public async Task<IActionResult> GetKillSwitchStatus(CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out _, out var errorResult))
            return errorResult!;

        var status = await _killSwitchService.GetStatusAsync(ct);
        return Ok(status);
    }
}
