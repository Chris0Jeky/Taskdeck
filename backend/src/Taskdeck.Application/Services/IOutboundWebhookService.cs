using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IOutboundWebhookService
{
    Task<Result<OutboundWebhookSubscriptionSecretDto>> CreateSubscriptionAsync(
        Guid boardId,
        Guid actorUserId,
        CreateOutboundWebhookSubscriptionDto dto,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<OutboundWebhookSubscriptionDto>>> ListSubscriptionsAsync(
        Guid boardId,
        CancellationToken cancellationToken = default);

    Task<Result<OutboundWebhookSubscriptionSecretDto>> RotateSecretAsync(
        Guid boardId,
        Guid subscriptionId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result> RevokeSubscriptionAsync(
        Guid boardId,
        Guid subscriptionId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result> EnqueueBoardMutationAsync(
        BoardRealtimeEvent mutation,
        CancellationToken cancellationToken = default);
}
