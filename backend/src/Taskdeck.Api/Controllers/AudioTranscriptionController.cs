using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/thinking-audio/{id:guid}/transcriptions")]
public class AudioTranscriptionController : AuthenticatedControllerBase
{
    private readonly AudioTranscriptionService service;
    public AudioTranscriptionController(AudioTranscriptionService service, IUserContext userContext) : base(userContext)
    { this.service = service; }

    [HttpGet]
    public async Task<IActionResult> Status(Guid id, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId, out var error)) return error!;
        Response.Headers.CacheControl = "no-store";
        var result = await service.StatusAsync(userId, id, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> Start(Guid id, AudioTranscriptionRequestDto request, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId, out var error)) return error!;
        Response.Headers.CacheControl = "no-store";
        var result = await service.StartAsync(userId, id, request, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
