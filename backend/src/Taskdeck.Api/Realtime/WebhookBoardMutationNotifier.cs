using Taskdeck.Application.Services;

namespace Taskdeck.Api.Realtime;

public sealed class WebhookBoardMutationNotifier : IBoardRealtimeNotifier
{
    private readonly IOutboundWebhookService _outboundWebhookService;
    private readonly ILogger<WebhookBoardMutationNotifier> _logger;

    public WebhookBoardMutationNotifier(
        IOutboundWebhookService outboundWebhookService,
        ILogger<WebhookBoardMutationNotifier> logger)
    {
        _outboundWebhookService = outboundWebhookService;
        _logger = logger;
    }

    public async Task NotifyBoardMutationAsync(
        BoardRealtimeEvent mutation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _outboundWebhookService.EnqueueBoardMutationAsync(mutation, cancellationToken);
            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Failed to enqueue outbound webhook deliveries for board mutation {EntityType}.{Operation} on board {BoardId}. ErrorCode={ErrorCode}",
                    mutation.EntityType,
                    mutation.Operation,
                    mutation.BoardId,
                    result.ErrorCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception while enqueueing outbound webhook deliveries for board mutation {EntityType}.{Operation} on board {BoardId}",
                mutation.EntityType,
                mutation.Operation,
                mutation.BoardId);
        }
    }
}
