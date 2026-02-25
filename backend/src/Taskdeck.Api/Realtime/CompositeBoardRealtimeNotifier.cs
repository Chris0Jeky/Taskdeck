using Taskdeck.Application.Services;

namespace Taskdeck.Api.Realtime;

public sealed class CompositeBoardRealtimeNotifier : IBoardRealtimeNotifier
{
    private readonly SignalRBoardRealtimeNotifier _signalRNotifier;
    private readonly WebhookBoardMutationNotifier _webhookNotifier;
    private readonly ILogger<CompositeBoardRealtimeNotifier> _logger;

    public CompositeBoardRealtimeNotifier(
        SignalRBoardRealtimeNotifier signalRNotifier,
        WebhookBoardMutationNotifier webhookNotifier,
        ILogger<CompositeBoardRealtimeNotifier> logger)
    {
        _signalRNotifier = signalRNotifier;
        _webhookNotifier = webhookNotifier;
        _logger = logger;
    }

    public async Task NotifyBoardMutationAsync(
        BoardRealtimeEvent mutation,
        CancellationToken cancellationToken = default)
    {
        await NotifySafeAsync(
            "signalr",
            mutation,
            cancellationToken,
            ct => _signalRNotifier.NotifyBoardMutationAsync(mutation, ct));

        await NotifySafeAsync(
            "webhook",
            mutation,
            cancellationToken,
            ct => _webhookNotifier.NotifyBoardMutationAsync(mutation, ct));
    }

    private async Task NotifySafeAsync(
        string channel,
        BoardRealtimeEvent mutation,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task> action)
    {
        try
        {
            await action(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed board mutation notification on channel {Channel} for {EntityType}.{Operation} (BoardId={BoardId})",
                channel,
                mutation.EntityType,
                mutation.Operation,
                mutation.BoardId);
        }
    }
}
