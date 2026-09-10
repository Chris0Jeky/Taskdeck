using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/thinking-audio")]
public class ThinkingAudioController : AuthenticatedControllerBase
{
    private readonly ThinkingAudioService service;
    public ThinkingAudioController(ThinkingAudioService service, IUserContext userContext) : base(userContext)
    { this.service = service; }

    [HttpGet("library")]
    public async Task<IActionResult> Library([FromQuery] int offset = 0, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var error)) return error!;
        Response.Headers.CacheControl = "no-store";
        var result = await service.LibraryAsync(userId, offset, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    [HttpGet("library/{id:guid}")]
    public async Task<IActionResult> LibraryDetail(Guid id, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId, out var error)) return error!;
        Response.Headers.CacheControl = "no-store";
        var result = await service.LibraryDetailAsync(userId, id, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    [HttpGet("library/{id:guid}/original")]
    public async Task<IActionResult> LibraryOriginal(Guid id, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId, out var error)) return error!;
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        var result = await service.LibraryDownloadAsync(userId, id, ct);
        return result.IsSuccess ? File(result.Value.Content, result.Value.MediaType, result.Value.FileName) : result.ToErrorActionResult();
    }

    [HttpGet("questions/{boardId:guid}/{cardId:guid}/{layerId:guid}")]
    public async Task<IActionResult> Get(Guid boardId, Guid cardId, Guid layerId, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId, out var error)) return error!;
        Response.Headers.CacheControl = "no-store";
        var result = await service.GetQuestionAsync(userId, boardId, cardId, layerId, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    [HttpPost("questions/{boardId:guid}/{cardId:guid}/{layerId:guid}")]
    [RequestSizeLimit(ThinkingAudioService.MaximumBytes)]
    public async Task<IActionResult> Upload(Guid boardId, Guid cardId, Guid layerId, [FromQuery] ThinkingAudioUploadDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId, out var error)) return error!;
        var result = await service.UploadAsync(userId, boardId, cardId, layerId, dto, Request.ContentType ?? "", Request.Body, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    [HttpPut("{id:guid}/written-version")]
    public async Task<IActionResult> Write(Guid id, ThinkingAudioWriteDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId, out var error)) return error!;
        var result = await service.WriteAsync(userId, id, dto, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, ThinkingAudioConfirmDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId, out var error)) return error!;
        var result = await service.ConfirmAsync(userId, id, dto, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    [HttpGet("{id:guid}/original")]
    public async Task<IActionResult> Original(Guid id, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId, out var error)) return error!;
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        var result = await service.DownloadAsync(userId, id, ct);
        return result.IsSuccess ? File(result.Value.Content, result.Value.MediaType, result.Value.FileName) : result.ToErrorActionResult();
    }
}
