using Microsoft.AspNetCore.Mvc;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// API endpoints for managing archived items and restoring them.
/// </summary>
[ApiController]
[Route("api/archive")]
public class ArchiveController : ControllerBase
{
    private readonly IArchiveRecoveryService _archiveService;

    public ArchiveController(IArchiveRecoveryService archiveService)
    {
        _archiveService = archiveService;
    }

    /// <summary>
    /// Gets a list of archived items with optional filters.
    /// </summary>
    /// <param name="entityType">Filter by entity type (e.g., "Card", "Board")</param>
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
    /// <param name="entityType">Entity type (e.g., "Card", "Board")</param>
    /// <param name="entityId">Entity ID to restore</param>
    /// <param name="restoredByUserId">User ID performing the restore</param>
    /// <param name="dto">Restore options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Restore result</returns>
    [HttpPost("{entityType}/{entityId}/restore")]
    public async Task<IActionResult> RestoreArchivedItem(
        string entityType,
        Guid entityId,
        [FromQuery] Guid restoredByUserId,
        [FromBody] RestoreArchiveItemDto dto,
        CancellationToken cancellationToken = default)
    {
        // Find the archive item by entity type and ID
        var archiveItems = await _archiveService.GetArchiveItemsAsync(
            entityType,
            null,
            null,
            1000,
            cancellationToken);

        if (!archiveItems.IsSuccess)
        {
            return archiveItems.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = archiveItems.ErrorCode, message = archiveItems.ErrorMessage }),
                "ValidationError" => BadRequest(new { errorCode = archiveItems.ErrorCode, message = archiveItems.ErrorMessage }),
                _ => Problem(archiveItems.ErrorMessage, statusCode: 500)
            };
        }

        var archiveItem = archiveItems.Value.FirstOrDefault(a => a.EntityId == entityId && a.RestoreStatus == RestoreStatus.Available);
        if (archiveItem == null)
        {
            return NotFound(new
            {
                errorCode = "NotFound",
                message = $"No archived {entityType} found with ID {entityId}"
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
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return Ok(result.Value);
    }
}
