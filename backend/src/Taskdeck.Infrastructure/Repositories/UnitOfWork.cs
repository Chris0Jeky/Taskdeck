using Microsoft.EntityFrameworkCore.Storage;
using Taskdeck.Application.Interfaces;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly TaskdeckDbContext _context;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(
        TaskdeckDbContext context,
        IBoardRepository boards,
        IColumnRepository columns,
        ICardRepository cards,
        ILabelRepository labels)
    {
        _context = context;
        Boards = boards;
        Columns = columns;
        Cards = cards;
        Labels = labels;
    }

    public IBoardRepository Boards { get; }
    public IColumnRepository Columns { get; }
    public ICardRepository Cards { get; }
    public ILabelRepository Labels { get; }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            await _transaction.CommitAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
}
