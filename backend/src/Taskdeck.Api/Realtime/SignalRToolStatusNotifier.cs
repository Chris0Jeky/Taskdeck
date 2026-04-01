using Microsoft.AspNetCore.SignalR;
using Taskdeck.Api.Hubs;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Realtime;

/// <summary>
/// Sends tool status events to the frontend via SignalR during multi-turn
/// tool-calling conversations. Clients receive a ToolStatusEvent with the
/// tool name, display message, and round progress.
/// </summary>
public sealed class SignalRToolStatusNotifier : IToolStatusNotifier
{
    private readonly IHubContext<BoardsHub> _hubContext;

    public SignalRToolStatusNotifier(IHubContext<BoardsHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyToolStatusAsync(
        Guid boardId,
        string toolName,
        string displayMessage,
        int round,
        int maxRounds,
        CancellationToken ct = default)
    {
        var groupName = BoardHubGroups.ForBoard(boardId);

        await _hubContext.Clients.Group(groupName).SendAsync(
            "toolStatus",
            new ToolStatusEvent(toolName, displayMessage, round, maxRounds),
            ct);
    }
}

/// <summary>
/// Event sent to the frontend via SignalR during tool-calling orchestration.
/// </summary>
public record ToolStatusEvent(
    string ToolName,
    string DisplayMessage,
    int Round,
    int MaxRounds
);
