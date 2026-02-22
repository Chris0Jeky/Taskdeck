using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Taskdeck.Api.Realtime;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using BoardAuthorizationService = Taskdeck.Application.Services.IAuthorizationService;

namespace Taskdeck.Api.Hubs;

[Authorize]
public class BoardsHub : Hub
{
    private readonly BoardAuthorizationService _authorizationService;
    private readonly IBoardPresenceTracker _presenceTracker;

    public BoardsHub(
        BoardAuthorizationService authorizationService,
        IBoardPresenceTracker presenceTracker)
    {
        _authorizationService = authorizationService;
        _presenceTracker = presenceTracker;
    }

    public async Task JoinBoard(Guid boardId)
    {
        var (userId, displayName) = ResolveCurrentUser();

        var permission = await _authorizationService.CanReadBoardAsync(userId, boardId);
        if (!permission.IsSuccess)
            throw new HubException($"{permission.ErrorCode}:{permission.ErrorMessage}");

        if (!permission.Value)
            throw new HubException($"{ErrorCodes.Forbidden}:You do not have access to this board");

        await Groups.AddToGroupAsync(Context.ConnectionId, BoardHubGroups.ForBoard(boardId));
        var presence = _presenceTracker.Join(boardId, Context.ConnectionId, userId, displayName);
        await PublishPresenceSnapshotAsync(presence);
    }

    public async Task LeaveBoard(Guid boardId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, BoardHubGroups.ForBoard(boardId));
        var presence = _presenceTracker.Leave(boardId, Context.ConnectionId);
        await PublishPresenceSnapshotAsync(presence);
    }

    public async Task SetEditingCard(Guid boardId, Guid? cardId)
    {
        if (!_presenceTracker.IsConnectionJoinedBoard(Context.ConnectionId, boardId))
            throw new HubException($"{ErrorCodes.Forbidden}:Join the board before sharing editing status");

        var sanitizedCardId = cardId.HasValue && cardId.Value != Guid.Empty ? cardId : null;
        var presence = _presenceTracker.UpdateEditingCard(boardId, Context.ConnectionId, sanitizedCardId);
        if (presence is null)
            throw new HubException($"{ErrorCodes.NotFound}:Board presence session not found");

        await PublishPresenceSnapshotAsync(presence);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var presence = _presenceTracker.LeaveConnection(Context.ConnectionId);
        if (presence is not null)
        {
            await PublishPresenceSnapshotAsync(presence);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private (Guid UserId, string DisplayName) ResolveCurrentUser()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId) || userId == Guid.Empty)
            throw new HubException($"{ErrorCodes.Unauthorized}:Authentication is required");

        var displayName =
            Context.User?.FindFirst("name")?.Value
            ?? Context.User?.Identity?.Name
            ?? Context.User?.FindFirst(ClaimTypes.Email)?.Value
            ?? userId.ToString("N")[..8];

        return (userId, displayName);
    }

    private async Task PublishPresenceSnapshotAsync(BoardPresenceSnapshot snapshot)
    {
        await Clients
            .Group(BoardHubGroups.ForBoard(snapshot.BoardId))
            .SendAsync("boardPresence", snapshot);
    }
}
