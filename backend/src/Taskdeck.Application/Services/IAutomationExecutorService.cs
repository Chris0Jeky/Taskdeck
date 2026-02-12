using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IAutomationExecutorService
{
    Task<Result> ExecuteProposalAsync(Guid proposalId, string idempotencyKey, CancellationToken cancellationToken = default);
}
