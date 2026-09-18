using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ManualRepresentationStoreTests
{
    [Fact]
    public async Task HumanTextAndConfirmationRetainSeparatePayloadsAndImmutableLineage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new TaskdeckDbContext(new DbContextOptionsBuilder<TaskdeckDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var owner = new User("representation-owner", "representation@example.test", "hash"); db.Users.Add(owner);
        var capture = new Capture(Guid.NewGuid(), owner.Id, CaptureModality.Audio, CaptureOriginAdapter.WebComposer,
            CaptureProducerKind.Human, CaptureIntentMode.Remember, CaptureSource.Voice);
        var asset = SourceAsset.FromBlobReference(capture.Id, 0, CaptureModality.Audio, "audio/webm", new string('a', 64), 4, Guid.NewGuid(), "original.webm");
        capture.AddSourceAsset(asset); db.Captures.Add(capture); await db.SaveChangesAsync();
        var store = new EfManualRepresentationStore(db);
        var text = new Transcript(owner.Id, CaptureSource.Typed, "My written\r\nversion", createdFromCaptureId: capture.Id);
        var header = Header(text, asset.Id);
        var noTransaction = () => store.StageTranscriptAsync(owner.Id, header, text, null, default);
        await noTransaction.Should().ThrowAsync<InvalidOperationException>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await store.StageTranscriptAsync(owner.Id, header, text, null, default); await db.SaveChangesAsync();
        var confirmed = new Transcript(owner.Id, CaptureSource.Typed, text.Text, createdFromCaptureId: capture.Id);
        var verified = Header(confirmed, null, header.Id, RepresentationQualityState.Verified);
        await store.StageTranscriptAsync(owner.Id, verified, confirmed, new RepresentationSupersession(header, verified), default);
        await db.SaveChangesAsync(); await tx.CommitAsync(); db.ChangeTracker.Clear();
        var rows = await store.ListByCaptureAsync(capture.Id, owner.Id);
        rows.Should().HaveCount(2);
        (await db.Representations.SingleAsync(x => x.Id == header.Id)).QualityState.Should().Be(RepresentationQualityState.Final);
        (await db.Representations.SingleAsync(x => x.Id == verified.Id)).ProcessingRunId.Should().BeNull("human confirmation does not invent an automated run");
        (await store.TranscriptAsync(text.Id, owner.Id, default))!.Text.Should().Be("My written\nversion");
        (await store.GetAsync(header.Id, Guid.NewGuid())).Should().BeNull();
        (await store.TranscriptAsync(confirmed.Id, Guid.NewGuid(), default)).Should().BeNull();
        (await store.ListByCaptureAsync(capture.Id, Guid.NewGuid())).Should().BeEmpty();
        await db.Captures.Where(x => x.Id == capture.Id).ExecuteDeleteAsync();
        (await db.Representations.CountAsync()).Should().Be(0);
        (await db.RepresentationSupersessions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RejectsForgedOwnerHashParentAndSecondSupersessionWithoutStagingPayload()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new TaskdeckDbContext(new DbContextOptionsBuilder<TaskdeckDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var owner = new User("representation-owner", "representation@example.test", "hash"); db.Users.Add(owner);
        var capture = new Capture(Guid.NewGuid(), owner.Id, CaptureModality.Text, CaptureOriginAdapter.WebComposer,
            CaptureProducerKind.Human, CaptureIntentMode.Remember, CaptureSource.Typed);
        var asset = capture.AddInlineTextSource("original"); db.Captures.Add(capture); await db.SaveChangesAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var store = new EfManualRepresentationStore(db);
        var text = new Transcript(owner.Id, CaptureSource.Typed, "written", createdFromCaptureId: capture.Id);
        var header = Header(text, asset.Id);
        await ((Func<Task>)(() => store.StageTranscriptAsync(Guid.NewGuid(), header, text, null, default))).Should().ThrowAsync<DomainException>();
        await ((Func<Task>)(() => store.StageTranscriptAsync(owner.Id, Header(text, Guid.NewGuid()), text, null, default))).Should().ThrowAsync<DomainException>();
        await ((Func<Task>)(() => store.StageTranscriptAsync(owner.Id, Header(text, asset.Id, hash: new string('a', 64)), text, null, default))).Should().ThrowAsync<DomainException>();
        db.ChangeTracker.Entries<Transcript>().Should().BeEmpty();
        await store.StageTranscriptAsync(owner.Id, header, text, null, default); await db.SaveChangesAsync();
        var nextText = new Transcript(owner.Id, CaptureSource.Typed, "corrected", createdFromCaptureId: capture.Id);
        var next = Header(nextText, null, header.Id);
        await store.StageTranscriptAsync(owner.Id, next, nextText, new RepresentationSupersession(header, next), default); await db.SaveChangesAsync();
        var staleText = new Transcript(owner.Id, CaptureSource.Typed, "stale", createdFromCaptureId: capture.Id);
        var stale = Header(staleText, null, header.Id);
        await ((Func<Task>)(() => store.StageTranscriptAsync(owner.Id, stale, staleText, new RepresentationSupersession(header, stale), default))).Should().ThrowAsync<DomainException>();
        (await db.Transcripts.CountAsync()).Should().Be(2);
        await tx.RollbackAsync();
        (await db.Representations.CountAsync()).Should().Be(0);
    }

    private static Representation Header(Transcript text, Guid? asset, Guid? parent = null,
        RepresentationQualityState quality = RepresentationQualityState.Final, string? hash = null) => new(
        text.Id, text.CreatedFromCaptureId, text.UserId, RepresentationKind.Transcript, asset, parent, null,
        "human-written", "1", null, Representation.ComputeTextContentHash("human-written-v1"), 1,
        hash ?? Representation.ComputeTextContentHash(text.Text), null, quality, [], DateTimeOffset.UtcNow);
}
