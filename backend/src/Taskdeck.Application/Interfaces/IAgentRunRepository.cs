using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IAgentRunRepository : IRepository<AgentRun>
{
    Task<IEnumerable<AgentRun>> GetByAgentProfileIdAsync(Guid agentProfileId, int limit = 100, CancellationToken cancellationToken = default);
    Task<AgentRun?> GetByIdWithEventsAsync(Guid id, CancellationToken cancellationToken = default);
}
