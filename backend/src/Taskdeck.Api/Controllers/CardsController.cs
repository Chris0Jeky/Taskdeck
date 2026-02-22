using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using BoardAuthorizationService = Taskdeck.Application.Services.IAuthorizationService;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/boards/{boardId}/cards")]
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

    [HttpGet]
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

    [HttpPost]
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

    [HttpPatch("{cardId}")]
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

    [HttpPost("{cardId}/move")]
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

    [HttpDelete("{cardId}")]
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
