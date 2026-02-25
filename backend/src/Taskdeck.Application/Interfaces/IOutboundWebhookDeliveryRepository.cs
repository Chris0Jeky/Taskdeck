using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IOutboundWebhookDeliveryRepository : IRepository<OutboundWebhookDelivery>
{
    Task<IReadOnlyList<OutboundWebhookDelivery>> GetDuePendingAsync(
        DateTimeOffset now,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboundWebhookDelivery>> GetBySubscriptionAsync(
        Guid subscriptionId,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboundWebhookDelivery>> GetStuckProcessingAsync(
        DateTimeOffset staleBefore,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<bool> TryClaimPendingAsync(
        Guid deliveryId,
        DateTimeOffset expectedUpdatedAt,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken = default);

    Task ReloadWithSubscriptionAsync(
        OutboundWebhookDelivery delivery,
        CancellationToken cancellationToken = default);
}
