using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface ICommandRunRepository : IRepository<CommandRun>
{
    Task<IEnumerable<CommandRun>> GetByUserIdAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<CommandRun>> GetByStatusAsync(CommandRunStatus status, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<CommandRun>> GetByTemplateNameAsync(string templateName, int limit = 100, CancellationToken cancellationToken = default);
    Task<CommandRun?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default);
    Task<CommandRun?> GetByIdWithLogsAsync(Guid id, CancellationToken cancellationToken = default);
}
