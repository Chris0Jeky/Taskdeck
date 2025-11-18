namespace Taskdeck.Application.Interfaces;

public interface IUnitOfWork
{
    IBoardRepository Boards { get; }
    IColumnRepository Columns { get; }
    ICardRepository Cards { get; }
    ILabelRepository Labels { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
