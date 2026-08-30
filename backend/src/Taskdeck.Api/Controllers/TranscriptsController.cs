using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Read-only access to the caller's own stored transcripts.
/// <para>
/// Backs the Review "view in transcript" affordance: the provenance drawer resolves a
/// transcript evidence link (source type <c>Transcript</c> plus a character span) by
/// fetching the transcript here and highlighting the span locally.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/transcripts")]
public class TranscriptsController : AuthenticatedControllerBase
{
    private readonly ITranscriptQueryService _transcriptQueryService;

    public TranscriptsController(
        ITranscriptQueryService transcriptQueryService,
        IUserContext userContext)
        : base(userContext)
    {
        _transcriptQueryService = transcriptQueryService;
    }

    /// <summary>
    /// Returns one transcript owned by the authenticated caller, including its full
    /// LF-normalized text and its line-indexed segments.
    /// </summary>
    /// <remarks>
    /// A transcript owned by another user returns 404, exactly as a nonexistent one does,
    /// so this endpoint cannot reveal that another user's transcript exists.
    /// </remarks>
    [HttpGet("{id:guid}")]
    [ResponseCache(NoStore = true)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _transcriptQueryService.GetForUserAsync(userId, id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
