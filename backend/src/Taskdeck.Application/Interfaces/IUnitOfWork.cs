namespace Taskdeck.Application.Interfaces;

public interface IUnitOfWork
{
    IBoardRepository Boards { get; }
    IColumnRepository Columns { get; }
    ICardRepository Cards { get; }
    ILabelRepository Labels { get; }
    IUserRepository Users { get; }
    IBoardAccessRepository BoardAccesses { get; }
    IAuditLogRepository AuditLogs { get; }
    ILlmQueueRepository LlmQueue { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
