using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class TodayController : AuthenticatedControllerBase
{
    private readonly IDailySealService _dailySealService;

    public TodayController(
        IDailySealService dailySealService,
        IUserContext userContext)
        : base(userContext)
    {
        _dailySealService = dailySealService;
    }

    [HttpPost("seal")]
    public async Task<IActionResult> SealDay([FromBody] SealDayRequest request)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _dailySealService.SealDayAsync(userId, request.Date);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("seal")]
    public async Task<IActionResult> GetSealStatus([FromQuery] DateOnly date)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _dailySealService.GetSealStatusAsync(userId, date);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}

public sealed record SealDayRequest(DateOnly Date);
