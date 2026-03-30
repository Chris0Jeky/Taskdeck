using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using BoardAuthorizationService = Taskdeck.Application.Services.IAuthorizationService;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Manage cards within a board. Cards represent individual work items and belong
/// to a column. Supports search, move, and capture provenance tracking.
/// </summary>
[ApiController]
[Authorize]
[Route("api/boards/{boardId}/cards")]
[Produces("application/json")]
public class CardsController : AuthenticatedControllerBase
{
    private readonly CardService _cardService;
    private readonly BoardAuthorizationService _authorizationService;

    public CardsController(
        CardService cardService,
        BoardAuthorizationService authorizationService,
        IUserContext userContext)
        : base(userContext)
    {
        _cardService = cardService;
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// Search cards on a board with optional filters.
    /// </summary>
    /// <param name="boardId">The board identifier.</param>
    /// <param name="search">Optional text search across card titles and descriptions.</param>
    /// <param name="labelId">Filter cards by label.</param>
    /// <param name="columnId">Filter cards by column.</param>
    /// <returns>A list of cards matching the criteria.</returns>
    /// <response code="200">Returns matching cards.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">User does not have read access to this board.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCards(
        Guid boardId,
        [FromQuery] string? search,
        [FromQuery] Guid? labelId,
        [FromQuery] Guid? columnId)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanReadBoardAsync(actorId, targetBoardId),
            "You do not have access to this board");

        if (permissionError is not null)
            return permissionError;

        var result = await _cardService.SearchCardsAsync(boardId, search, labelId, columnId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Get capture provenance for a card, showing the link back to the
    /// capture item and proposal that created it.
    /// </summary>
    /// <param name="boardId">The board identifier.</param>
    /// <param name="cardId">The card identifier.</param>
    /// <returns>Provenance details linking the card to its capture origin.</returns>
    /// <response code="200">Returns the provenance record.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">User does not have read access to this board.</response>
    /// <response code="404">Card or provenance not found.</response>
    [HttpGet("{cardId}/provenance")]
    [ProducesResponseType(typeof(CardCaptureProvenanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCardProvenance(Guid boardId, Guid cardId)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanReadBoardAsync(actorId, targetBoardId),
            "You do not have access to this board");

        if (permissionError is not null)
            return permissionError;

        var result = await _cardService.GetCaptureProvenanceAsync(boardId, cardId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Create a new card on a board.
    /// </summary>
    /// <param name="boardId">The board identifier.</param>
    /// <param name="dto">Card creation parameters including title, column, and optional labels.</param>
    /// <returns>The newly created card.</returns>
    /// <response code="201">Card created successfully.</response>
    /// <response code="400">Validation error (e.g., WIP limit exceeded).</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">User does not have write access to this board.</response>
    [HttpPost]
    [ProducesResponseType(typeof(CardDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateCard(Guid boardId, [FromBody] CreateCardDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanWriteBoardAsync(actorId, targetBoardId),
            "You do not have permission to modify this board");

        if (permissionError is not null)
            return permissionError;

        var createDto = dto with { BoardId = boardId };
        var result = await _cardService.CreateCardAsync(createDto);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetCards), new { boardId }, result.Value)
            : result.ToErrorActionResult();
    }

    /// <summary>
    /// Update card fields. Supports optimistic concurrency via ExpectedUpdatedAt.
    /// </summary>
    /// <param name="boardId">The board identifier.</param>
    /// <param name="cardId">The card identifier.</param>
    /// <param name="dto">Fields to update (all optional). Include ExpectedUpdatedAt for conflict detection.</param>
    /// <returns>The updated card.</returns>
    /// <response code="200">Card updated successfully.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">User does not have write access to this board.</response>
    /// <response code="404">Card not found.</response>
    /// <response code="409">Conflict — the card was modified since ExpectedUpdatedAt.</response>
    [HttpPatch("{cardId}")]
    [ProducesResponseType(typeof(CardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCard(Guid boardId, Guid cardId, [FromBody] UpdateCardDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanWriteBoardAsync(actorId, targetBoardId),
            "You do not have permission to modify this board");

        if (permissionError is not null)
            return permissionError;

        var result = await _cardService.UpdateCardAsync(boardId, cardId, dto, userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Move a card to a different column and/or position.
    /// </summary>
    /// <param name="boardId">The board identifier.</param>
    /// <param name="cardId">The card identifier.</param>
    /// <param name="dto">Target column and position.</param>
    /// <returns>The moved card with updated column and position.</returns>
    /// <response code="200">Card moved successfully.</response>
    /// <response code="400">Validation error (e.g., WIP limit exceeded on target column).</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">User does not have write access to this board.</response>
    [HttpPost("{cardId}/move")]
    [ProducesResponseType(typeof(CardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> MoveCard(Guid boardId, Guid cardId, [FromBody] MoveCardDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanWriteBoardAsync(actorId, targetBoardId),
            "You do not have permission to modify this board");

        if (permissionError is not null)
            return permissionError;

        var result = await _cardService.MoveCardAsync(boardId, cardId, dto);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Delete a card from a board.
    /// </summary>
    /// <param name="boardId">The board identifier.</param>
    /// <param name="cardId">The card identifier.</param>
    /// <response code="204">Card deleted successfully.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">User does not have write access to this board.</response>
    /// <response code="404">Card not found.</response>
    [HttpDelete("{cardId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCard(Guid boardId, Guid cardId)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanWriteBoardAsync(actorId, targetBoardId),
            "You do not have permission to modify this board");

        if (permissionError is not null)
            return permissionError;

        var result = await _cardService.DeleteCardAsync(boardId, cardId);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }
}
