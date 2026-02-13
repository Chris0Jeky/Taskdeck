using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Route("api/boards/{boardId}/cards")]
public class CardsController : ControllerBase
{
    private readonly CardService _cardService;

    public CardsController(CardService cardService)
    {
        _cardService = cardService;
    }

    [HttpGet]
    public async Task<IActionResult> GetCards(
        Guid boardId,
        [FromQuery] string? search,
        [FromQuery] Guid? labelId,
        [FromQuery] Guid? columnId)
    {
        var result = await _cardService.SearchCardsAsync(boardId, search, labelId, columnId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> CreateCard(Guid boardId, [FromBody] CreateCardDto dto)
    {
        var createDto = dto with { BoardId = boardId };
        var result = await _cardService.CreateCardAsync(createDto);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetCards), new { boardId }, result.Value)
            : result.ToErrorActionResult();
    }

    [HttpPatch("{cardId}")]
    public async Task<IActionResult> UpdateCard(Guid boardId, Guid cardId, [FromBody] UpdateCardDto dto)
    {
        var result = await _cardService.UpdateCardAsync(boardId, cardId, dto);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost("{cardId}/move")]
    public async Task<IActionResult> MoveCard(Guid boardId, Guid cardId, [FromBody] MoveCardDto dto)
    {
        var result = await _cardService.MoveCardAsync(boardId, cardId, dto);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpDelete("{cardId}")]
    public async Task<IActionResult> DeleteCard(Guid boardId, Guid cardId)
    {
        var result = await _cardService.DeleteCardAsync(boardId, cardId);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }
}
