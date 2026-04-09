using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Manages MCP API keys for HTTP transport authentication.
/// Keys are scoped to the authenticated user and use the <c>tdsk_</c> prefix.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ApiKeysController : AuthenticatedControllerBase
{
    private readonly ApiKeyService _apiKeyService;

    public ApiKeysController(ApiKeyService apiKeyService, IUserContext userContext)
        : base(userContext)
    {
        _apiKeyService = apiKeyService;
    }

    /// <summary>
    /// Create a new API key. The plaintext key is returned once and cannot be retrieved again.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateApiKeyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new ApiErrorResponse(ErrorCodes.ValidationError, "Name is required"));

        TimeSpan? expiresIn = request.ExpiresInDays.HasValue
            ? TimeSpan.FromDays(request.ExpiresInDays.Value)
            : null;

        try
        {
            var (plaintextKey, entity) = await _apiKeyService.CreateKeyAsync(
                userId, request.Name, expiresIn, cancellationToken);

            return StatusCode(StatusCodes.Status201Created, new CreateApiKeyResponse(
                entity.Id,
                plaintextKey,
                entity.KeyPrefix_,
                entity.Name,
                entity.CreatedAt,
                entity.ExpiresAt));
        }
        catch (DomainException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message));
        }
    }

    /// <summary>List all API keys for the authenticated user.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ListApiKeysResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var keys = await _apiKeyService.ListKeysAsync(userId, cancellationToken);

        var items = keys.Select(k => new ApiKeyListItem(
            k.Id,
            k.KeyPrefix_,
            k.Name,
            k.CreatedAt,
            k.ExpiresAt,
            k.RevokedAt,
            k.LastUsedAt,
            k.IsActive));

        return Ok(new ListApiKeysResponse(items.ToList()));
    }

    /// <summary>Revoke an API key.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        try
        {
            await _apiKeyService.RevokeKeyAsync(id, userId, cancellationToken);
            return NoContent();
        }
        catch (DomainException ex) when (ex.ErrorCode == ErrorCodes.NotFound)
        {
            return NotFound(new ApiErrorResponse(ex.ErrorCode, ex.Message));
        }
        catch (DomainException ex) when (ex.ErrorCode == ErrorCodes.Forbidden)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ApiErrorResponse(ex.ErrorCode, ex.Message));
        }
        catch (DomainException ex)
        {
            return BadRequest(new ApiErrorResponse(ex.ErrorCode, ex.Message));
        }
    }
}

// ── Request / Response contracts ──────────────────────────────────────────────

public sealed record CreateApiKeyRequest(string Name, int? ExpiresInDays = null);

public sealed record CreateApiKeyResponse(
    Guid Id,
    string Key,
    string KeyPrefix,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record ApiKeyListItem(
    Guid Id,
    string KeyPrefix,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt,
    bool IsActive);

public sealed record ListApiKeysResponse(List<ApiKeyListItem> Keys);
