using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class SearchController : AuthenticatedControllerBase
{
    private readonly ISearchService _searchService;

    public SearchController(ISearchService searchService, IUserContext userContext) : base(userContext)
    {
        _searchService = searchService;
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] int maxResults = 20,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _searchService.SearchAsync(
            userId, q ?? string.Empty, maxResults, offset, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
