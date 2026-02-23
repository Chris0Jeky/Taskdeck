using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using BoardAuthorizationService = Taskdeck.Application.Services.IAuthorizationService;

namespace Taskdeck.Api.Hubs;

[Authorize]
public class BoardsHub : Hub
{
    private readonly BoardAuthorizationService _authorizationService;

    public BoardsHub(BoardAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
    }

    public async Task JoinBoard(Guid boardId)
    {
        var userId = ResolveCurrentUserId();

        var permission = await _authorizationService.CanReadBoardAsync(userId, boardId);
        if (!permission.IsSuccess)
            throw new HubException($"{permission.ErrorCode}:{permission.ErrorMessage}");

        if (!permission.Value)
            throw new HubException($"{ErrorCodes.Forbidden}:You do not have access to this board");

        await Groups.AddToGroupAsync(Context.ConnectionId, BoardHubGroups.ForBoard(boardId));
    }

    public async Task LeaveBoard(Guid boardId)
    {
        var userId = ResolveCurrentUserId();

        var permission = await _authorizationService.CanReadBoardAsync(userId, boardId);
        if (!permission.IsSuccess)
            throw new HubException($"{permission.ErrorCode}:{permission.ErrorMessage}");

        if (!permission.Value)
            throw new HubException($"{ErrorCodes.Forbidden}:You do not have access to this board");

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, BoardHubGroups.ForBoard(boardId));
    }

    private Guid ResolveCurrentUserId()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId) || userId == Guid.Empty)
            throw new HubException($"{ErrorCodes.Unauthorized}:Authentication is required");

        return userId;
    }
}
