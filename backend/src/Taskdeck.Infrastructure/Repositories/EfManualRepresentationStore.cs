using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class EfManualRepresentationStore(TaskdeckDbContext db) : IManualRepresentationStore
{
    public Task<Representation?> HeaderAsync(Guid id, Guid actorId, CancellationToken ct) =>
        db.Representations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == actorId, ct);
    public async Task StageTranscriptAsync(Guid actorId, Representation header, Transcript payload, RepresentationSupersession? supersession, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Representation writes require a transaction.");
        if (header.UserId != actorId || header.CaptureId is not { } captureId || payload.CreatedFromCaptureId != captureId)
            throw new DomainException(ErrorCodes.ValidationError, "Representation ownership and capture must match.");
        header.ValidatePayload(payload);
        if (header.ContentHash != Representation.ComputeTextContentHash(payload.Text))
            throw new DomainException(ErrorCodes.ValidationError, "Representation content hash does not match its transcript.");
        var capture = await db.Captures.SingleOrDefaultAsync(x => x.Id == captureId && x.UserId == actorId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "This source is unavailable.");
        if (header.ParentSourceAssetId is { } assetId)
        {
            var asset = await db.SourceAssets.SingleOrDefaultAsync(x => x.Id == assetId && x.CaptureId == captureId, ct)
                ?? throw new DomainException(ErrorCodes.NotFound, "This source is unavailable.");
            header.ValidateParent(asset, capture);
        }
        else
        {
            var parent = await db.Representations.SingleOrDefaultAsync(x => x.Id == header.ParentRepresentationId && x.UserId == actorId, ct)
                ?? throw new DomainException(ErrorCodes.NotFound, "This representation is unavailable.");
            header.ValidateParent(parent);
        }
        if (supersession is not null)
        {
            var previous = await db.Representations.SingleOrDefaultAsync(x => x.Id == supersession.RepresentationId && x.UserId == actorId, ct)
                ?? throw new DomainException(ErrorCodes.NotFound, "This representation is unavailable.");
            var checkedEdge = new RepresentationSupersession(previous, header);
            if (checkedEdge.SupersededByRepresentationId != supersession.SupersededByRepresentationId
                || await db.RepresentationSupersessions.AnyAsync(x => x.RepresentationId == previous.Id, ct))
                throw new DomainException(ErrorCodes.Conflict, "The representation changed. Reload before confirming.");
            db.RepresentationSupersessions.Add(supersession);
        }
        db.Transcripts.Add(payload);
        db.Representations.Add(header);
    }

    public Task<Transcript?> TranscriptAsync(Guid id, Guid actorId, CancellationToken ct) =>
        db.Transcripts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == actorId, ct);

    public async Task<IReadOnlyList<RepresentationDescriptor>> ListByCaptureAsync(Guid captureId, Guid userId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Representations.AsNoTracking().Where(x => x.CaptureId == captureId && x.UserId == userId).OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var ids = rows.Select(x => x.Id).ToArray();
        var edges = await db.RepresentationSupersessions.AsNoTracking().Where(x => ids.Contains(x.RepresentationId)).ToDictionaryAsync(x => x.RepresentationId, cancellationToken);
        return rows.Select(row => RepresentationDescriptor.FromRepresentation(row, edges.GetValueOrDefault(row.Id))).ToList();
    }

    public async Task<RepresentationDescriptor?> GetAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var row = await db.Representations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        if (row is null) return null;
        var edge = await db.RepresentationSupersessions.AsNoTracking().SingleOrDefaultAsync(x => x.RepresentationId == id, cancellationToken);
        return RepresentationDescriptor.FromRepresentation(row, edge);
    }
}
