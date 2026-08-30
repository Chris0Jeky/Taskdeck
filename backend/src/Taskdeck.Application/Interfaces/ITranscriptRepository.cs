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
    /// Returns the subset of <paramref name="transcriptIds"/> owned by <paramref name="userId"/>.
    /// <para>
    /// Ids the user does not own are simply absent from the result, so the caller cannot tell a
    /// missing transcript from another user's transcript. Reads ids only — never transcript text.
    /// </para>
    /// </summary>
    Task<IReadOnlyCollection<Guid>> FilterOwnedIdsAsync(
        IReadOnlyCollection<Guid> transcriptIds,
        Guid userId,
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
