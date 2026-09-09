using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class ChatSourceReader(TaskdeckDbContext db) : IChatSourceReader
{
    public Task<ChatMemorySnapshot?> MemoryAsync(Guid actorId, Guid memoryId, CancellationToken ct) =>
        db.Set<WorkspaceMemory>().AsNoTracking().Where(memory => memory.UserId == actorId && memory.Id == memoryId)
            .Select(memory => new ChatMemorySnapshot(memory.Id, memory.BoardId, memory.Title, memory.Text,
                memory.Status, memory.Revision, memory.Archived, memory.SourceCaptureId)).SingleOrDefaultAsync(ct);

    private IQueryable<SourceAsset> Sources(Guid actorId, Guid boardId, Guid captureId) =>
        db.Set<SourceAsset>().AsNoTracking().Where(asset => asset.CaptureId == captureId &&
            asset.StorageKind == SourceAssetStorageKind.InlineText && asset.TextPayload != null &&
            db.Set<Capture>().Any(capture => capture.Id == asset.CaptureId && capture.UserId == actorId &&
                capture.ContextBoardId == boardId && capture.LegacyRequestId == null));

    private static IQueryable<ChatAssetSnapshot> Project(IQueryable<SourceAsset> assets) => assets.Select(asset =>
        new ChatAssetSnapshot(asset.Id, asset.OriginalName ?? "Original text", asset.ContentHash, asset.ByteSize,
            asset.SupersededByAssetId, asset.TextPayload!.Text.Length > 1500 ? asset.TextPayload.Text.Substring(0, 1500) : asset.TextPayload.Text,
            asset.TextPayload.Text.Length > 1500));

    public async Task<IReadOnlyList<ChatAssetSnapshot>> AssetsAsync(Guid actorId, Guid boardId, Guid captureId, int offset, CancellationToken ct) =>
        await Project(Sources(actorId, boardId, captureId).OrderBy(asset => asset.Ordinal).ThenBy(asset => asset.Id).Skip(offset).Take(11)).ToListAsync(ct);

    public Task<ChatAssetSnapshot?> AssetAsync(Guid actorId, Guid boardId, Guid captureId, Guid assetId, CancellationToken ct) =>
        Project(Sources(actorId, boardId, captureId).Where(asset => asset.Id == assetId)).SingleOrDefaultAsync(ct);
}
