using Microsoft.AspNetCore.Mvc;
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
        return result.IsSuccess ? Ok(result.Value) : Problem(result.ErrorMessage, statusCode: 500);
    }

    [HttpPost]
    public async Task<IActionResult> CreateCard(Guid boardId, [FromBody] CreateCardDto dto)
    {
        var createDto = dto with { BoardId = boardId };
        var result = await _cardService.CreateCardAsync(createDto);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "WipLimitExceeded" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return CreatedAtAction(nameof(GetCards), new { boardId }, result.Value);
    }

    [HttpPatch("{cardId}")]
    public async Task<IActionResult> UpdateCard(Guid boardId, Guid cardId, [FromBody] UpdateCardDto dto)
    {
        var result = await _cardService.UpdateCardAsync(cardId, dto);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return Ok(result.Value);
    }

    [HttpPost("{cardId}/move")]
    public async Task<IActionResult> MoveCard(Guid boardId, Guid cardId, [FromBody] MoveCardDto dto)
    {
        var result = await _cardService.MoveCardAsync(cardId, dto);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "WipLimitExceeded" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return Ok(result.Value);
    }

    [HttpDelete("{cardId}")]
    public async Task<IActionResult> DeleteCard(Guid boardId, Guid cardId)
    {
        var result = await _cardService.DeleteCardAsync(cardId);

        if (!result.IsSuccess)
        {
            return result.ErrorCode == "NotFound"
                ? NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
                : Problem(result.ErrorMessage, statusCode: 500);
        }

        return NoContent();
    }
}
