using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface ITranscriptRepository : IRepository<Transcript>
{
    Task<Transcript?> GetByIdForUserAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Transcript>> GetByUserAsync(
        Guid userId,
        int limit = 500,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the character count of both persisted transcript text and segment
    /// metadata, so buffered export can bound its complete serialized payload.
    /// </summary>
    Task<long> GetEstimatedSerializedLengthByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<int> DeleteByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
