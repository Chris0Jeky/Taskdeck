using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class TranscriptRepository : Repository<Transcript>, ITranscriptRepository
{
    public TranscriptRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public Task<Transcript?> GetByIdForUserAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
        => _dbSet.AsNoTracking().FirstOrDefaultAsync(
            transcript => transcript.Id == id && transcript.UserId == userId,
            cancellationToken);

    public async Task<IReadOnlyList<Transcript>> GetByUserAsync(
        Guid userId,
        int limit = 500,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var boundedLimit = Math.Clamp(limit, 1, 500);
        var boundedOffset = Math.Max(offset, 0);

        // SQLite does not reliably translate DateTimeOffset ordering. Id gives every
        // caller, including GDPR export paging, a stable provider-independent order.
        return await _dbSet
            .AsNoTracking()
            .Where(transcript => transcript.UserId == userId)
            .OrderBy(transcript => transcript.Id)
            .Skip(boundedOffset)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<long> GetEstimatedSerializedLengthByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var total = await _dbSet
            .Where(transcript => transcript.UserId == userId)
            .SumAsync(
                transcript => (long?)(transcript.Text.Length + transcript.SegmentsJson.Length),
                cancellationToken);
        return total ?? 0L;
    }

    public Task<int> DeleteByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => _dbSet.Where(transcript => transcript.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetIdsByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
        => await _dbSet
            .AsNoTracking()
            .Where(transcript => transcript.UserId == userId)
            .Select(transcript => transcript.Id)
            .ToListAsync(cancellationToken);
}
