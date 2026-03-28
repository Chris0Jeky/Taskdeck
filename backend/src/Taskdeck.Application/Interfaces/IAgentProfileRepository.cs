using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IAgentProfileRepository : IRepository<AgentProfile>
{
    Task<IEnumerable<AgentProfile>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
