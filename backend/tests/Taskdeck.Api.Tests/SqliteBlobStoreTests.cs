using System.Security.Cryptography;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Storage;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class SqliteBlobStoreTests
{
    private sealed class Harness : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:");
        public TaskdeckDbContext Db { get; private set; } = null!;
        public User Owner { get; } = new("audio-owner", "owner@example.test", "test-hash");
        public User Other { get; } = new("audio-other", "other@example.test", "test-hash");
        public BlobStorageSettings Settings { get; } = new();
        public SqliteBlobStore Store => new(Db, Settings);
        public static async Task<Harness> Create()
        {
            var result = new Harness();
            await result.Connection.OpenAsync();
            result.Db = new(new DbContextOptionsBuilder<TaskdeckDbContext>().UseSqlite(result.Connection).Options);
            await result.Db.Database.EnsureCreatedAsync();
            result.Db.Users.AddRange(result.Owner, result.Other); await result.Db.SaveChangesAsync();
            return result;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
        public BlobAcquisition Request(long size, Guid? owner = null) => new(owner ?? Owner.Id, CaptureModality.Audio, size, "contract-test", null);
    }

    private sealed class BoundedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int Reads { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            buffer.Length.Should().BeLessThanOrEqualTo(StoredBlobChunk.MaximumSize);
            Reads++;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    [Fact]
    public async Task LargerThanBufferRoundTripUsesBoundedChunksAndOwnerScopedReads()
    {
        await using var h = await Harness.Create();
        var bytes = RandomNumberGenerator.GetBytes(StoredBlobChunk.MaximumSize * 5 + 13);
        using var input = new BoundedStream(bytes);
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        var source = await h.Store.AcquireAsync(h.Request(bytes.Length), input);
        await tx.CommitAsync();
        input.Reads.Should().BeGreaterThan(5);
        input.CanRead.Should().BeTrue("the caller owns the input stream");
        source.ContentHash.Should().Be(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        (await h.Db.StoredBlobChunks.MaxAsync(x => x.Content.Length)).Should().BeLessThanOrEqualTo(StoredBlobChunk.MaximumSize);
        await using var output = (await h.Store.OpenReferenceReadAsync(source.ReferenceId, h.Owner.Id))!;
        using var received = new MemoryStream(); await output.CopyToAsync(received);
        received.ToArray().Should().Equal(bytes);
        (await h.Store.OpenReferenceReadAsync(source.ReferenceId, h.Other.Id)).Should().BeNull();
        (await h.Store.OpenReadAsync(source.BlobObjectId, h.Other.Id)).Should().BeNull();
        (await h.Store.FindByHashAsync(h.Other.Id, source.ContentHash)).Should().BeNull();
    }

    [Fact]
    public async Task DedupeIsPerOwnerAndOnlyLastReferenceReleaseDeletesBytes()
    {
        await using var h = await Harness.Create();
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var first = await h.Store.AcquireAsync(h.Request(4), new MemoryStream(bytes));
        var second = await h.Store.AcquireAsync(h.Request(4), new MemoryStream(bytes));
        var foreign = await h.Store.AcquireAsync(h.Request(4, h.Other.Id), new MemoryStream(bytes));
        first.BlobObjectId.Should().Be(second.BlobObjectId).And.NotBe(foreign.BlobObjectId);
        first.ReferenceId.Should().NotBe(second.ReferenceId);
        (await h.Store.GetUsageAsync(h.Owner.Id)).Should().Match<BlobQuotaUsage>(x => x.TotalBytes == 4 && x.ReferenceCount == 2 && x.ObjectCount == 1);
        var unauthorized = async () => await h.Store.ReleaseAsync(first.ReferenceId, h.Other.Id);
        await unauthorized.Should().ThrowAsync<DomainException>();
        (await h.Store.ReleaseAsync(first.ReferenceId, h.Owner.Id)).Should().BeFalse();
        (await h.Store.FindByHashAsync(h.Owner.Id, first.ContentHash))!.ReferenceCount.Should().Be(1);
        (await h.Store.ReleaseAsync(second.ReferenceId, h.Owner.Id)).Should().BeTrue();
        (await h.Store.FindByHashAsync(h.Owner.Id, first.ContentHash)).Should().BeNull();
        (await h.Db.StoredBlobChunks.CountAsync(x => x.BlobId == first.BlobObjectId)).Should().Be(0);
        (await h.Store.OpenReferenceReadAsync(foreign.ReferenceId, h.Other.Id)).Should().NotBeNull();
        await tx.CommitAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task MismatchedLengthRollsBackUploadEvenWhenCallerCommits(int actualSize)
    {
        await using var h = await Harness.Create();
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        var upload = async () => await h.Store.AcquireAsync(h.Request(4), new MemoryStream(new byte[actualSize]));
        await upload.Should().ThrowAsync<DomainException>();
        await tx.CommitAsync();
        (await h.Db.StoredBlobs.CountAsync()).Should().Be(0);
        (await h.Db.StoredBlobChunks.CountAsync()).Should().Be(0);
        (await h.Db.StoredBlobReferences.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task QuotaIsRefusedBeforeReadingAndRollbackRemovesAllUploadRows()
    {
        await using var h = await Harness.Create();
        h.Settings.OwnerQuotaBytes = 5;
        await using (var tx = await h.Db.Database.BeginTransactionAsync())
        {
            using var input = new BoundedStream(new byte[6]);
            var upload = async () => await h.Store.AcquireAsync(h.Request(6), input);
            await upload.Should().ThrowAsync<DomainException>().Where(x => x.ErrorCode == ErrorCodes.PayloadTooLarge);
            input.Reads.Should().Be(0);
            await h.Store.AcquireAsync(h.Request(4), new MemoryStream(new byte[4]));
            await tx.RollbackAsync();
        }
        (await h.Db.StoredBlobs.CountAsync()).Should().Be(0);
        (await h.Db.StoredBlobChunks.CountAsync()).Should().Be(0);
        (await h.Db.StoredBlobReferences.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AcquiringExistingReferenceAndAccountErasurePreserveOtherOwners()
    {
        await using var h = await Harness.Create();
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        var first = await h.Store.AcquireAsync(h.Request(4), new MemoryStream(new byte[4]));
        var again = await h.Store.AcquireExistingAsync(h.Owner.Id, first.ContentHash, CaptureModality.Audio, "another-source", null);
        again!.BlobObjectId.Should().Be(first.BlobObjectId);
        var other = await h.Store.AcquireAsync(h.Request(4, h.Other.Id), new MemoryStream(new byte[4]));
        await h.Db.Users.Where(x => x.Id == h.Owner.Id).ExecuteDeleteAsync();
        (await h.Db.StoredBlobs.CountAsync()).Should().Be(1);
        (await h.Db.StoredBlobReferences.CountAsync()).Should().Be(1);
        (await h.Store.OpenReferenceReadAsync(other.ReferenceId, h.Other.Id)).Should().NotBeNull();
        await tx.CommitAsync();
    }

    [Fact]
    public async Task WritesRequireAnAmbientTransactionAndDoNotFlushOtherTrackedWork()
    {
        await using var h = await Harness.Create();
        var withoutTransaction = async () => await h.Store.AcquireAsync(h.Request(4), new MemoryStream(new byte[4]));
        await withoutTransaction.Should().ThrowAsync<InvalidOperationException>();
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        h.Db.Users.Add(new User("not-saved", "pending@example.test", "test-hash"));
        await h.Store.AcquireAsync(h.Request(4), new MemoryStream(new byte[4]));
        (await h.Db.Users.CountAsync()).Should().Be(2);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task TwoWritersDeduplicateWithoutLosingEitherReference()
    {
        var path = Path.Combine(Path.GetTempPath(), $"taskdeck-blob-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>().UseSqlite($"Data Source={path};Pooling=False;Default Timeout=15").Options;
        var owner = new User("concurrent-audio", "concurrent@example.test", "test-hash");
        try
        {
            await using (var setup = new TaskdeckDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync(); setup.Users.Add(owner); await setup.SaveChangesAsync();
            }
            var bytes = RandomNumberGenerator.GetBytes(StoredBlobChunk.MaximumSize * 2 + 3);
            var work = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
            {
                await using var db = new TaskdeckDbContext(options);
                await using var tx = await db.Database.BeginTransactionAsync();
                var result = await new SqliteBlobStore(db, new()).AcquireAsync(new(owner.Id, CaptureModality.Audio, bytes.Length, "race", null), new BoundedStream(bytes));
                await tx.CommitAsync(); return result;
            }));
            var results = await Task.WhenAll(work);
            results.Select(x => x.BlobObjectId).Distinct().Should().HaveCount(1);
            results.Select(x => x.ReferenceId).Distinct().Should().HaveCount(2);
            await using var verify = new TaskdeckDbContext(options);
            (await verify.StoredBlobs.CountAsync()).Should().Be(1);
            (await verify.StoredBlobReferences.CountAsync()).Should().Be(2);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task AccountEndpointErasesBytesEvenThoughTheUserRowIsRetained()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(client, "blob-erasure-owner");
        using var otherClient = factory.CreateClient();
        var other = await ApiTestHarness.AuthenticateAsync(otherClient, "blob-erasure-other");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<IBlobStore>();
            await using var tx = await db.Database.BeginTransactionAsync();
            foreach (var id in new[] { owner.UserId, other.UserId })
                await store.AcquireAsync(new(id, CaptureModality.Audio, 3, "account-contract", null), new MemoryStream([1, 2, 3]));
            await tx.CommitAsync();
        }
        (await client.PostAsJsonAsync("/api/account/delete", new AccountDeletionRequest("password123", "DELETE MY ACCOUNT"))).EnsureSuccessStatusCode();
        using var finalScope = factory.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await finalDb.Users.FindAsync(owner.UserId))!.IsActive.Should().BeFalse();
        (await finalDb.StoredBlobs.Select(x => x.OwnerUserId).ToListAsync()).Should().Equal(other.UserId);
        (await finalDb.StoredBlobReferences.Select(x => x.OwnerUserId).ToListAsync()).Should().Equal(other.UserId);
        (await finalDb.StoredBlobChunks.CountAsync()).Should().Be(1);
    }
}
