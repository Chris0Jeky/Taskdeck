using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
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
public class ArchiveController : AuthenticatedControllerBase
{
    private readonly IArchiveRecoveryService _archiveService;

    public ArchiveController(
        IArchiveRecoveryService archiveService,
        IUserContext userContext) : base(userContext)
    {
        _archiveService = archiveService;
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
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var result = await _archiveService.GetArchiveItemsAsync(
            entityType,
            boardId,
            status,
            limit,
            callerUserId,
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
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
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var result = await _archiveService.GetArchiveItemByIdAsync(id, callerUserId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
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
            restoredByUserId,
            cancellationToken);

        if (!archiveItemResult.IsSuccess)
            return archiveItemResult.ToErrorActionResult();

        var archiveItem = archiveItemResult.Value;
        if (archiveItem.RestoreStatus != RestoreStatus.Available)
        {
            return Conflict(new ApiErrorResponse(
                ErrorCodes.InvalidOperation,
                $"Archive item for {normalizedEntityType} with ID {entityId} is in status {archiveItem.RestoreStatus}"));
        }

        var result = await _archiveService.RestoreArchiveItemAsync(
            archiveItem.Id,
            dto,
            restoredByUserId,
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    private static bool TryNormalizeEntityType(string entityType, out string normalizedEntityType, out IActionResult? errorResult)
    {
        normalizedEntityType = string.Empty;
        errorResult = null;

        if (string.IsNullOrWhiteSpace(entityType))
        {
            errorResult = new BadRequestObjectResult(new ApiErrorResponse(
                ErrorCodes.ValidationError,
                "EntityType is required"));
            return false;
        }

        normalizedEntityType = entityType.Trim().ToLowerInvariant();
        if (normalizedEntityType != "board" && normalizedEntityType != "column" && normalizedEntityType != "card")
        {
            errorResult = new BadRequestObjectResult(new ApiErrorResponse(
                ErrorCodes.ValidationError,
                "EntityType must be 'board', 'column', or 'card'"));
            return false;
        }

        return true;
    }
}
