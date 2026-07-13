using ContentDispositionHeaderValue = System.Net.Http.Headers.ContentDispositionHeaderValue;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.Filters;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/artefacts")]
public class ArtefactsController : AuthenticatedControllerBase
{
    private readonly IArtefactService _artefactService;
    private readonly ArtefactStorageSettings _settings;

    public ArtefactsController(
        IArtefactService artefactService,
        ArtefactStorageSettings settings,
        IUserContext userContext)
        : base(userContext)
    {
        _artefactService = artefactService;
        _settings = settings;
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [DisableFormValueModelBinding]
    [EnableRateLimiting(RateLimitingPolicyNames.CaptureWritePerUser)]
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var upload = await ArtefactMultipartReader.ReadAsync(Request, _settings, cancellationToken);
        if (!upload.IsSuccess)
            return upload.ToErrorActionResult();

        await using var stream = new MemoryStream(upload.Value.Content, writable: false);
        var result = await _artefactService.CreateAsync(
            userId,
            new CreateArtefactRequest(
                stream,
                upload.Value.FileName,
                upload.Value.MimeType,
                upload.Value.BoardId,
                upload.Value.CreatedFromCaptureId),
            cancellationToken);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetMetadata), new { id = result.Value.Id }, result.Value)
            : result.ToErrorActionResult();
    }

    [HttpGet("{id:guid}")]
    [ResponseCache(NoStore = true)]
    public async Task<IActionResult> GetMetadata(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _artefactService.GetMetadataAsync(userId, id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("{id:guid}/content")]
    [ResponseCache(NoStore = true)]
    public async Task<IActionResult> GetContent(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var metadata = await _artefactService.GetMetadataAsync(userId, id, cancellationToken);
        if (!metadata.IsSuccess)
            return metadata.ToErrorActionResult();

        Response.ContentType = metadata.Value.MimeType;
        Response.ContentLength = metadata.Value.ByteSize;
        var disposition = new ContentDispositionHeaderValue(
            metadata.Value.Kind == ArtefactKind.Image ? "inline" : "attachment")
        {
            FileNameStar = metadata.Value.FileName
        };
        Response.Headers.ContentDisposition = disposition.ToString();

        var result = await _artefactService.CopyContentAsync(
            userId,
            id,
            Response.Body,
            cancellationToken);
        if (!result.IsSuccess && !Response.HasStarted)
        {
            Response.ContentType = null;
            Response.ContentLength = null;
            Response.Headers.Remove(HeaderNames.ContentDisposition);
            return result.ToErrorActionResult();
        }

        return new EmptyResult();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _artefactService.DeleteAsync(userId, id, cancellationToken);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }
}
