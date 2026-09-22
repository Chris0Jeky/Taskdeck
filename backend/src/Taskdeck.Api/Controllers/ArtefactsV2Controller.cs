using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Length-aware streaming upload. The existing multipart /api/artefacts route remains
/// available to clients that omit a part length or send the file before metadata.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v2/artefacts")]
public sealed class ArtefactsV2Controller : AuthenticatedControllerBase
{
    private readonly IArtefactService _artefacts;
    private readonly ArtefactStorageSettings _settings;
    private static readonly TimeSpan UploadDeadline = TimeSpan.FromMinutes(2);

    public ArtefactsV2Controller(IArtefactService artefacts, ArtefactStorageSettings settings,
        IUserContext userContext) : base(userContext)
    {
        _artefacts = artefacts;
        _settings = settings;
    }

    /// <summary>
    /// POST a raw file body with an exact Content-Length and its media type in Content-Type.
    /// Supply fileName and optional boardId/createdFromCaptureId as query parameters.
    /// No body bytes are read before authorization, metadata checks and quota reservation.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitingPolicyNames.CaptureWritePerUser)]
    public async Task<IActionResult> Upload(
        [FromQuery] string? fileName,
        [FromQuery] string? boardId,
        [FromQuery] string? createdFromCaptureId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        if (Request.ContentLength is not long length || length <= 0)
            return Invalid("A positive Content-Length is required for streaming uploads").ToErrorActionResult();
        if (length > _settings.MaxBytesPerArtefact)
            return Result.Failure(ErrorCodes.PayloadTooLarge,
                $"Artefact exceeds the configured {_settings.MaxBytesPerArtefact}-byte size limit")
                .ToErrorActionResult();
        if (string.IsNullOrWhiteSpace(Request.ContentType))
            return Invalid("Artefact content type is required").ToErrorActionResult();
        if (!string.IsNullOrWhiteSpace(Request.Headers["Content-Encoding"]) &&
            !string.Equals(Request.Headers["Content-Encoding"], "identity", StringComparison.OrdinalIgnoreCase))
            return Invalid("Compressed request bodies are not supported").ToErrorActionResult();
        if (!TryOptionalId(boardId, out var parsedBoardId) ||
            !TryOptionalId(createdFromCaptureId, out var parsedCaptureId))
            return Invalid("Artefact board or capture ID is invalid").ToErrorActionResult();

        var bodySize = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false })
            bodySize.MaxRequestBodySize = _settings.MaxBytesPerArtefact;

        // A quota reservation precedes body reads; the body is staged outside SQLite.
        // Cap a stalled client so it cannot retain the reservation indefinitely.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(UploadDeadline);
        try
        {
            var result = await _artefacts.CreateStreamingAsync(userId,
                new CreateStreamingArtefactRequest(Request.Body, fileName ?? string.Empty,
                    Request.ContentType, length, parsedBoardId, parsedCaptureId), deadline.Token);
            return result.IsSuccess
                ? Created($"/api/artefacts/{result.Value.Id}", result.Value)
                : result.ToErrorActionResult();
        }
        catch (BadHttpRequestException error) when (error.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Result.Failure(ErrorCodes.PayloadTooLarge,
                $"Artefact exceeds the configured {_settings.MaxBytesPerArtefact}-byte size limit")
                .ToErrorActionResult();
        }
        catch (BadHttpRequestException)
        {
            return Invalid("Artefact upload body ended unexpectedly").ToErrorActionResult();
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status408RequestTimeout,
                new ApiErrorResponse(ErrorCodes.ValidationError, "Streaming upload timed out"));
        }
    }

    private static bool TryOptionalId(string? input, out Guid? id)
    {
        id = null;
        if (input is null) return true;
        if (!Guid.TryParse(input, out var parsed) || parsed == Guid.Empty) return false;
        id = parsed;
        return true;
    }

    private static Result Invalid(string message) => Result.Failure(ErrorCodes.ValidationError, message);
}
