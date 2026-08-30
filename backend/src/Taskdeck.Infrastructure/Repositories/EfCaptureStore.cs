using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICaptureStore"/> over the <c>Captures</c> aggregate
/// (<c>Captures</c>, <c>SourceAssets</c>, <c>SourceAssetTextPayloads</c>). Shares the scoped
/// <see cref="TaskdeckDbContext"/> with <see cref="UnitOfWork"/>, so a staged capture and its
/// assets commit in the same <c>SaveChangesAsync</c> as the queue row it mirrors.
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
        // Adding the root stages the aggregate: source assets through the auto-included navigation,
        // text payloads through the asset's one-to-one navigation.
        await _context.Captures.AddAsync(capture, cancellationToken);
    }

    public Task<Capture?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
        => _context.Captures
            .AsNoTracking()
            .Include(capture => capture.SourceAssets)
            .ThenInclude(asset => asset.TextPayload)
            .FirstOrDefaultAsync(capture => capture.Id == id && capture.UserId == userId, cancellationToken);

    public Task<Capture?> GetByIdForUpdateAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
        => _context.Captures
            .Include(capture => capture.SourceAssets)
            .ThenInclude(asset => asset.TextPayload)
            .FirstOrDefaultAsync(capture => capture.Id == id && capture.UserId == userId, cancellationToken);

    public Task UpdateAsync(Capture capture, CancellationToken cancellationToken = default)
    {
        // A tracked aggregate is already staged by the change tracker; Update() re-attaches one that
        // was read detached (or arrived from another scope) so either read path commits the same way.
        // The write itself belongs to IUnitOfWork.SaveChangesAsync, exactly like AddAsync.
        var entry = _context.Entry(capture);
        if (entry.State == EntityState.Detached)
        {
            _context.Captures.Update(capture);
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Capture>> GetByIdsForUserAsync(
        IReadOnlyCollection<Guid> ids,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<Capture>();
        }

        var distinctIds = ids.Distinct().ToArray();
        return await _context.Captures
            .AsNoTracking()
            .Include(capture => capture.SourceAssets)
            .ThenInclude(asset => asset.TextPayload)
            .Where(capture => capture.UserId == userId && distinctIds.Contains(capture.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CaptureListMaterial>> GetListMaterialForUserAsync(
        IReadOnlyCollection<Guid> ids,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<CaptureListMaterial>();
        }

        var distinctIds = ids.Distinct().ToArray();

        // Two narrow queries instead of one aggregate load. The second one filters on
        // SupersededByAssetId IS NULL in SQL, so a correction history never reaches the client:
        // an Inbox page pays for one text per capture, not for every revision of it.
        var headers = await _context.Captures
            .AsNoTracking()
            .Where(capture => capture.UserId == userId && distinctIds.Contains(capture.Id))
            .Select(capture => new
            {
                capture.Id,
                capture.LegacySourceSnapshot,
                capture.CapturedAtServer,
                capture.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        if (headers.Count == 0)
        {
            return Array.Empty<CaptureListMaterial>();
        }

        var headerIds = headers.Select(header => header.Id).ToArray();
        var activeText = await _context.SourceAssets
            .AsNoTracking()
            .Where(asset => headerIds.Contains(asset.CaptureId)
                && asset.SupersededByAssetId == null
                && asset.StorageKind == SourceAssetStorageKind.InlineText)
            .Select(asset => new
            {
                asset.CaptureId,
                asset.Ordinal,
                Text = asset.TextPayload!.Text
            })
            .ToListAsync(cancellationToken);

        // Last active inline asset by ordinal, matching Capture.CurrentText.
        var textByCapture = activeText
            .GroupBy(asset => asset.CaptureId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(asset => asset.Ordinal).First().Text);

        return headers
            .Select(header => new CaptureListMaterial(
                header.Id,
                header.LegacySourceSnapshot,
                header.CapturedAtServer,
                header.UpdatedAt,
                textByCapture.TryGetValue(header.Id, out var text) ? text : null))
            .ToList();
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.Captures.AsNoTracking().AnyAsync(capture => capture.Id == id, cancellationToken);

    public Task<int> CountByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => _context.Captures.AsNoTracking().CountAsync(capture => capture.UserId == userId, cancellationToken);

    public async Task<int> DeleteByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Set-based, children first: explicit rather than relying on the database honouring the
        // cascade, so the erasure is the same on every provider the store may run on.
        var ownedAssets = _context.SourceAssets
            .Where(asset => _context.Captures.Any(capture => capture.Id == asset.CaptureId && capture.UserId == userId));

        await _context.SourceAssetTextPayloads
            .Where(payload => ownedAssets.Any(asset => asset.Id == payload.SourceAssetId))
            .ExecuteDeleteAsync(cancellationToken);
        await ownedAssets.ExecuteDeleteAsync(cancellationToken);

        return await _context.Captures
            .Where(capture => capture.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
