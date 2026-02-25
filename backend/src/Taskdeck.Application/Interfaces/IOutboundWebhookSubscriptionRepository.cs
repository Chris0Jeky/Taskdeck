using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IOutboundWebhookSubscriptionRepository : IRepository<OutboundWebhookSubscription>
{
    Task<OutboundWebhookSubscription?> GetByIdForBoardAsync(
        Guid boardId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboundWebhookSubscription>> GetActiveByBoardAsync(
        Guid boardId,
        CancellationToken cancellationToken = default);
}
