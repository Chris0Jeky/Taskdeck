using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class ConnectorCredentialRepository : IConnectorCredentialRepository
{
    private readonly TaskdeckDbContext _context;

    public ConnectorCredentialRepository(TaskdeckDbContext context)
    {
        _context = context;
    }

    public async Task<ConnectorCredential?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ConnectorCredentials
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public Task<IEnumerable<ConnectorCredential>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        // Credential queries MUST be user-scoped. Use GetByUserIdAsync or GetByConnectorIdForUserAsync instead.
        throw new NotSupportedException(
            "Unfiltered credential access is not permitted. Use GetByUserIdAsync or GetByConnectorIdForUserAsync.");
    }

    public async Task<ConnectorCredential> AddAsync(ConnectorCredential entity, CancellationToken cancellationToken = default)
    {
        await _context.ConnectorCredentials.AddAsync(entity, cancellationToken);
        return entity;
    }

    public Task UpdateAsync(ConnectorCredential entity, CancellationToken cancellationToken = default)
    {
        _context.ConnectorCredentials.Update(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(ConnectorCredential entity, CancellationToken cancellationToken = default)
    {
        _context.ConnectorCredentials.Remove(entity);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ConnectorCredential>> GetByConnectorIdAsync(
        Guid connectorId,
        CancellationToken cancellationToken = default)
    {
        // Credential queries MUST be user-scoped. Use GetByConnectorIdForUserAsync instead.
        throw new NotSupportedException(
            "Unfiltered credential access by connector ID is not permitted. Use GetByConnectorIdForUserAsync.");
    }

    public async Task<ConnectorCredential?> GetByConnectorIdForUserAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ConnectorCredentials
            .FirstOrDefaultAsync(c => c.ConnectorId == connectorId && c.UserId == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<ConnectorCredential>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ConnectorCredentials
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ConnectorCredential?> GetByConnectorIdAndUserIdAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ConnectorCredentials
            .FirstOrDefaultAsync(c => c.ConnectorId == connectorId && c.UserId == userId, cancellationToken);
    }

    public async Task DeleteByConnectorIdAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var credentials = await _context.ConnectorCredentials
            .Where(c => c.ConnectorId == connectorId && c.UserId == userId)
            .ToListAsync(cancellationToken);

        _context.ConnectorCredentials.RemoveRange(credentials);
    }
}
