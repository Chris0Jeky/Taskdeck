namespace Taskdeck.Application.Interfaces;

public sealed record ChatMemorySnapshot(Guid Id, Guid BoardId, string Title, string Text, string Status,
    int Revision, bool Archived, Guid? CaptureId);
public sealed record ChatAssetSnapshot(Guid Id, string Name, string ContentHash, long ByteSize,
    Guid? SupersededByAssetId, string Excerpt, bool Truncated, int Ordinal = 0);

/// <summary>Bounded, untracked context reads; never loads a memory history or capture graph.</summary>
public interface IChatSourceReader
{
    Task<ChatMemorySnapshot?> MemoryAsync(Guid actorId, Guid memoryId, CancellationToken ct);
    Task<IReadOnlyList<ChatAssetSnapshot>> AssetsAsync(Guid actorId, Guid boardId, Guid captureId, int offset, CancellationToken ct);
    Task<IReadOnlyList<ChatAssetSnapshot>> AssetsAfterAsync(Guid actorId, Guid boardId, Guid captureId, int afterOrdinal, CancellationToken ct);
    Task<ChatAssetSnapshot?> AssetAsync(Guid actorId, Guid boardId, Guid captureId, Guid assetId, CancellationToken ct);
}
