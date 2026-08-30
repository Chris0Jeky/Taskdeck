using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICaptureStore"/> over the <c>Captures</c> table. Shares the
/// scoped <see cref="TaskdeckDbContext"/> with <see cref="UnitOfWork"/>, so a staged capture commits
/// in the same <c>SaveChangesAsync</c> as the queue row it mirrors.
/// </summary>
public sealed class EfCaptureStore : ICaptureStore
{
    private readonly TaskdeckDbContext _context;

    public EfCaptureStore(TaskdeckDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(Capture capture, CancellationToken cancellationToken = default)
    {
        await _context.Captures.AddAsync(capture, cancellationToken);
    }

    public Task<Capture?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
        => _context.Captures
            .AsNoTracking()
            .FirstOrDefaultAsync(capture => capture.Id == id && capture.UserId == userId, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.Captures.AsNoTracking().AnyAsync(capture => capture.Id == id, cancellationToken);

    public Task<int> CountByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => _context.Captures.AsNoTracking().CountAsync(capture => capture.UserId == userId, cancellationToken);
}
