using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// API endpoints for managing archived items and restoring them.
/// </summary>
[ApiController]
[Authorize]
[Route("api/archive")]
public class ArchiveController : ControllerBase
{
    private readonly IArchiveRecoveryService _archiveService;
    private readonly IUserContext _userContext;

    public ArchiveController(
        IArchiveRecoveryService archiveService,
        IUserContext userContext)
    {
        _archiveService = archiveService;
        _userContext = userContext;
    }

    /// <summary>
    /// Gets a list of archived items with optional filters.
    /// </summary>
    /// <param name="entityType">Filter by entity type (board, column, card)</param>
    /// <param name="boardId">Filter by board ID</param>
    /// <param name="status">Filter by restore status</param>
    /// <param name="limit">Maximum number of results (default: 100)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of archive items</returns>
    [HttpGet("items")]
    public async Task<IActionResult> GetArchiveItems(
        [FromQuery] string? entityType,
        [FromQuery] Guid? boardId,
        [FromQuery] RestoreStatus? status,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await _archiveService.GetArchiveItemsAsync(
            entityType,
            boardId,
            status,
            limit,
            cancellationToken);

        if (result.IsSuccess)
            return Ok(result.Value);

        return result.ErrorCode switch
        {
            "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
            "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
            _ => Problem(result.ErrorMessage, statusCode: 500)
        };
    }

    /// <summary>
    /// Gets a specific archive item by ID.
    /// </summary>
    /// <param name="id">Archive item ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Archive item details</returns>
    [HttpGet("items/{id}")]
    public async Task<IActionResult> GetArchiveItem(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _archiveService.GetArchiveItemByIdAsync(id, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ErrorCode == "NotFound"
                ? NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
                : Problem(result.ErrorMessage, statusCode: 500);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Restores an archived item.
    /// </summary>
    /// <param name="entityType">Entity type (board, column, card)</param>
    /// <param name="entityId">Entity ID to restore</param>
    /// <param name="dto">Restore options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Restore result</returns>
    [HttpPost("{entityType}/{entityId}/restore")]
    public async Task<IActionResult> RestoreArchivedItem(
        string entityType,
        Guid entityId,
        [FromBody] RestoreArchiveItemDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeEntityType(entityType, out var normalizedEntityType, out var invalidTypeResult))
            return invalidTypeResult!;

        if (!TryGetCurrentUserId(out var restoredByUserId, out var userErrorResult))
            return userErrorResult!;

        var archiveItemResult = await _archiveService.GetArchiveItemByEntityAsync(
            normalizedEntityType,
            entityId,
            cancellationToken);

        if (!archiveItemResult.IsSuccess)
        {
            return archiveItemResult.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = archiveItemResult.ErrorCode, message = archiveItemResult.ErrorMessage }),
                "ValidationError" => BadRequest(new { errorCode = archiveItemResult.ErrorCode, message = archiveItemResult.ErrorMessage }),
                "AuthenticationFailed" => Unauthorized(new { errorCode = archiveItemResult.ErrorCode, message = archiveItemResult.ErrorMessage }),
                "Unauthorized" => Unauthorized(new { errorCode = archiveItemResult.ErrorCode, message = archiveItemResult.ErrorMessage }),
                "Forbidden" => StatusCode(403, new { errorCode = archiveItemResult.ErrorCode, message = archiveItemResult.ErrorMessage }),
                _ => Problem(archiveItemResult.ErrorMessage, statusCode: 500)
            };
        }

        var archiveItem = archiveItemResult.Value;
        if (archiveItem.RestoreStatus != RestoreStatus.Available)
        {
            return Conflict(new
            {
                errorCode = ErrorCodes.InvalidOperation,
                message = $"Archive item for {normalizedEntityType} with ID {entityId} is in status {archiveItem.RestoreStatus}"
            });
        }

        var result = await _archiveService.RestoreArchiveItemAsync(
            archiveItem.Id,
            dto,
            restoredByUserId,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Conflict" => Conflict(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                ErrorCodes.InvalidOperation => Conflict(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "AuthenticationFailed" => Unauthorized(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Unauthorized" => Unauthorized(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Forbidden" => StatusCode(403, new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return Ok(result.Value);
    }

    private static bool TryNormalizeEntityType(string entityType, out string normalizedEntityType, out IActionResult? errorResult)
    {
        normalizedEntityType = string.Empty;
        errorResult = null;

        if (string.IsNullOrWhiteSpace(entityType))
        {
            errorResult = new BadRequestObjectResult(new
            {
                errorCode = ErrorCodes.ValidationError,
                message = "EntityType is required"
            });
            return false;
        }

        normalizedEntityType = entityType.Trim().ToLowerInvariant();
        if (normalizedEntityType != "board" && normalizedEntityType != "column" && normalizedEntityType != "card")
        {
            errorResult = new BadRequestObjectResult(new
            {
                errorCode = ErrorCodes.ValidationError,
                message = "EntityType must be 'board', 'column', or 'card'"
            });
            return false;
        }

        return true;
    }

    private bool TryGetCurrentUserId(out Guid userId, out IActionResult? errorResult)
    {
        userId = Guid.Empty;
        errorResult = null;

        if (!_userContext.IsAuthenticated || string.IsNullOrWhiteSpace(_userContext.UserId))
        {
            errorResult = Unauthorized(new
            {
                errorCode = ErrorCodes.AuthenticationFailed,
                message = "Authenticated user context is required"
            });
            return false;
        }

        if (!Guid.TryParse(_userContext.UserId, out userId))
        {
            errorResult = Unauthorized(new
            {
                errorCode = ErrorCodes.AuthenticationFailed,
                message = "Authenticated user id claim is invalid"
            });
            return false;
        }

        return true;
    }
}
