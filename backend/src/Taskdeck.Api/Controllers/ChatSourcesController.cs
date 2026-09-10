using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/llm/chat/context-memory")]
public class ChatSourcesController : AuthenticatedControllerBase
{
    private readonly ChatContextResolver context;
    public ChatSourcesController(ChatContextResolver context, IUserContext userContext) : base(userContext)
    {
        this.context = context;
    }

    [HttpGet("{id:guid}/sources")]
    [ResponseCache(NoStore = true)]
    public async Task<IActionResult> Sources(Guid id, [FromQuery] Guid boardId, [FromQuery] int revision,
        [FromQuery] int offset, CancellationToken ct, [FromQuery] int? afterOrdinal = null)
    {
        if (!TryGetCurrentUserId(out var actor, out var error)) return error!;
        var result = await context.ListSourcesAsync(actor, boardId, id, revision, offset, ct, afterOrdinal);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
