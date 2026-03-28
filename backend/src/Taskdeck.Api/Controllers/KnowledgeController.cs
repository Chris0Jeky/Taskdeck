using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/knowledge")]
public class KnowledgeController : AuthenticatedControllerBase
{
    private readonly IKnowledgeService _knowledgeService;

    public KnowledgeController(
        IKnowledgeService knowledgeService,
        IUserContext userContext) : base(userContext)
    {
        _knowledgeService = knowledgeService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateDocument(
        [FromBody] CreateKnowledgeDocumentDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _knowledgeService.CreateDocumentAsync(userId, dto, cancellationToken);
        return result.IsSuccess ? Created($"/api/knowledge/{result.Value.Id}", result.Value) : result.ToErrorActionResult();
    }

    [HttpGet]
    public async Task<IActionResult> ListDocuments(
        [FromQuery] Guid? boardId = null,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _knowledgeService.ListDocumentsAsync(userId, boardId, limit, offset, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDocument(Guid id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _knowledgeService.GetDocumentAsync(userId, id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateDocument(
        Guid id,
        [FromBody] UpdateKnowledgeDocumentDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _knowledgeService.UpdateDocumentAsync(userId, id, dto, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> ArchiveDocument(Guid id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _knowledgeService.ArchiveDocumentAsync(userId, id, cancellationToken);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchDocuments(
        [FromQuery] string q = "",
        [FromQuery] Guid? boardId = null,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _knowledgeService.SearchDocumentsAsync(userId, q, boardId, limit, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
