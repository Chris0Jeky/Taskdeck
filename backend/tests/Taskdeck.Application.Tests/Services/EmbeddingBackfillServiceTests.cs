using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Services;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class EmbeddingBackfillServiceTests
{
    private readonly Mock<IVectorIndex> _vectorIndexMock;
    private readonly Mock<IEmbeddingGenerator> _embeddingGeneratorMock;
    private readonly Mock<IKnowledgeChunkRepository> _chunkRepoMock;
    private readonly Mock<IKnowledgeDocumentRepository> _docRepoMock;
    private readonly InMemoryLogger<EmbeddingBackfillService> _logger;
    private readonly EmbeddingBackfillService _sut;

    // Each test uses unique chunk IDs (via Guid.NewGuid) so the static
    // _indexedChunkIds set does not interfere across tests.

    public EmbeddingBackfillServiceTests()
    {
        EmbeddingBackfillService.ResetProgressForTests();

        _vectorIndexMock = new Mock<IVectorIndex>();
        _embeddingGeneratorMock = new Mock<IEmbeddingGenerator>();
        _chunkRepoMock = new Mock<IKnowledgeChunkRepository>();
        _docRepoMock = new Mock<IKnowledgeDocumentRepository>();
        _logger = new InMemoryLogger<EmbeddingBackfillService>();

        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(true);
        _embeddingGeneratorMock.Setup(g => g.Dimensions).Returns(64);
        _vectorIndexMock
            .Setup(v => v.UpsertBatchAsync(
                It.IsAny<IReadOnlyList<VectorDocument>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _vectorIndexMock
            .Setup(v => v.UpsertAsync(
                It.IsAny<string>(),
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _vectorIndexMock
            .Setup(v => v.DeleteBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sut = new EmbeddingBackfillService(
            _vectorIndexMock.Object,
            _embeddingGeneratorMock.Object,
            _chunkRepoMock.Object,
            _docRepoMock.Object,
            _logger);
    }

    private KnowledgeDocument CreateDocument(Guid docId, Guid userId, Guid? boardId = null)
    {
        return new KnowledgeDocument(
            userId, "Test Doc", "Test content",
            KnowledgeSourceType.Manual, boardId);
    }

    private void SetupDocumentLookup(Guid docId, Guid userId, Guid? boardId = null)
    {
        var doc = CreateDocument(docId, userId, boardId);
        _docRepoMock
            .Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);
    }

    private void SetupChunkBatch(IReadOnlyList<KnowledgeChunk> chunks)
    {
        _chunkRepoMock
            .Setup(r => r.GetExistingIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.ToHashSet());

        _chunkRepoMock
            .Setup(r => r.GetUnindexedBatchAsync(
                It.IsAny<KnowledgeChunkBackfillCursor?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgeChunkBackfillCursor? cursor, int batchSize, CancellationToken _) =>
                GetBatchAfterCursor(chunks, cursor, batchSize));

        _chunkRepoMock
            .Setup(r => r.CountUnindexedAsync(
                It.IsAny<KnowledgeChunkBackfillCursor?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgeChunkBackfillCursor? cursor, CancellationToken _) =>
                CountAfterCursor(chunks, cursor));
    }

    [Fact]
    public async Task ProcessBatchAsync_EmbeddingGeneratorUnavailable_ReturnsZeroResult()
    {
        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(false);

        var result = await _sut.ProcessBatchAsync(batchSize: 10);

        result.Processed.Should().Be(0);
        result.Failed.Should().Be(0);
        result.Remaining.Should().Be(0);
    }

    [Fact]
    public async Task ProcessBatchAsync_NoChunks_ReturnsZeroResult()
    {
        SetupChunkBatch(Array.Empty<KnowledgeChunk>());

        var result = await _sut.ProcessBatchAsync(batchSize: 10);

        result.Processed.Should().Be(0);
        result.Failed.Should().Be(0);
    }

    [Fact]
    public async Task ProcessBatchAsync_ChunksExist_EmbeddsAndUpsertsEach()
    {
        var docId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chunks = new List<KnowledgeChunk>
        {
            new(docId, 0, "chunk zero content"),
            new(docId, 1, "chunk one content")
        };

        SetupChunkBatch(chunks);

        SetupDocumentLookup(docId, userId);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 0.5f, 0.5f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) =>
                texts.Select(_ => fakeEmbedding).ToList());

        var result = await _sut.ProcessBatchAsync(batchSize: 10);

        result.Processed.Should().Be(2);
        result.Failed.Should().Be(0);

        _vectorIndexMock.Verify(
            v => v.UpsertBatchAsync(
                It.Is<IReadOnlyList<VectorDocument>>(docs => docs.Count == 2 &&
                    docs.All(d => d.DocumentId.StartsWith("chunk:"))),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessBatchAsync_BatchSizeLimitsProcessed()
    {
        var docId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chunks = Enumerable.Range(0, 10)
            .Select(i => new KnowledgeChunk(docId, i, $"content {i}"))
            .ToList();

        SetupChunkBatch(chunks);

        SetupDocumentLookup(docId, userId);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 0.5f, 0.5f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) =>
                texts.Select(_ => fakeEmbedding).ToList());

        var result = await _sut.ProcessBatchAsync(batchSize: 3);

        result.Processed.Should().Be(3);
        result.Remaining.Should().Be(7);
    }

    [Fact]
    public async Task ProcessBatchAsync_CreatedAtCursorDoesNotSkipNextChunk_WhenEarlierChunkIsDeleted()
    {
        var docId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var chunks = new List<KnowledgeChunk>
        {
            new(docId, 0, "chunk zero content"),
            new(docId, 1, "chunk one content"),
            new(docId, 2, "chunk two content")
        };

        for (var i = 0; i < chunks.Count; i++)
        {
            SetCreatedAt(chunks[i], createdAt.AddSeconds(i));
        }

        SetupChunkBatch(chunks);
        SetupDocumentLookup(docId, userId);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 0.5f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) =>
                texts.Select(_ => fakeEmbedding).ToList());

        await _sut.ProcessBatchAsync(batchSize: 1);
        chunks.RemoveAt(0);

        var secondResult = await _sut.ProcessBatchAsync(batchSize: 1);

        secondResult.Processed.Should().Be(1);
        _vectorIndexMock.Verify(
            v => v.UpsertBatchAsync(
                It.Is<IReadOnlyList<VectorDocument>>(docs => docs.Any(d => d.DocumentId == $"chunk:{chunks[0].Id}")),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "backfill progress should use a stable cursor instead of a table-size-dependent offset");
    }

    [Fact]
    public async Task ProcessBatchAsync_UsesUnindexedBatchRepositoryInsteadOfFullTableScan()
    {
        var docId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chunks = Enumerable.Range(0, 5)
            .Select(i => new KnowledgeChunk(docId, i, $"content {i}"))
            .ToList();

        SetupChunkBatch(chunks);
        SetupDocumentLookup(docId, userId);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 0.5f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) =>
                texts.Select(_ => fakeEmbedding).ToList());

        await _sut.ProcessBatchAsync(batchSize: 2);

        _chunkRepoMock.Verify(
            r => r.GetUnindexedBatchAsync(
                It.Is<KnowledgeChunkBackfillCursor?>(cursor => cursor == null),
                2,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _chunkRepoMock.Verify(
            r => r.GetAllAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "backfill should fetch only the next unindexed page");
    }

    [Fact]
    public async Task ProcessBatchAsync_PrunesTrackedStaleVectorBeforeNextBatch()
    {
        var docId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chunk = new KnowledgeChunk(docId, 0, "content");
        var chunks = new[] { chunk };

        SetupChunkBatch(chunks);
        SetupDocumentLookup(docId, userId);

        _chunkRepoMock
            .Setup(r => r.GetExistingIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids.Where(id => id != chunk.Id).ToHashSet());

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 0.5f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) =>
                texts.Select(_ => fakeEmbedding).ToList());

        await _sut.ProcessBatchAsync(batchSize: 10);
        await _sut.ProcessBatchAsync(batchSize: 10);

        _vectorIndexMock.Verify(
            v => v.DeleteBatchAsync(
                It.Is<IReadOnlyList<string>>(ids => ids.Contains($"chunk:{chunk.Id}")),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "tracked vectors whose chunks disappear should be removed from the index");
    }

    [Fact]
    public async Task ProcessBatchAsync_BatchEmbeddingFails_FallsBackToOneByOne()
    {
        var docId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chunks = new List<KnowledgeChunk>
        {
            new(docId, 0, "good content"),
            new(docId, 1, "bad content"),
            new(docId, 2, "also good content")
        };

        SetupChunkBatch(chunks);

        SetupDocumentLookup(docId, userId);

        // Batch embedding fails, triggering one-by-one fallback
        _embeddingGeneratorMock
            .Setup(g => g.GenerateBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("batch embedding failed"));

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 0.5f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateAsync("good content", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);
        _embeddingGeneratorMock
            .Setup(g => g.GenerateAsync("also good content", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);
        _embeddingGeneratorMock
            .Setup(g => g.GenerateAsync("bad content", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embedding failed"));

        var result = await _sut.ProcessBatchAsync(batchSize: 10);

        result.Processed.Should().Be(2, "two of three chunks should succeed in one-by-one fallback");
        result.Failed.Should().Be(1, "the 'bad content' chunk should fail");
    }

    [Fact]
    public async Task ProcessBatchAsync_CursorUsesIdTieBreaker_ForChunksWithSameCreatedAt()
    {
        var docId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var chunks = Enumerable.Range(0, 3)
            .Select(i => new KnowledgeChunk(docId, i, $"content {i}"))
            .ToList();

        foreach (var chunk in chunks)
        {
            SetCreatedAt(chunk, createdAt);
        }

        var ordered = chunks.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToList();
        SetupChunkBatch(chunks);
        SetupDocumentLookup(docId, userId);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 0.5f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) =>
                texts.Select(_ => fakeEmbedding).ToList());

        await _sut.ProcessBatchAsync(batchSize: 1);
        await _sut.ProcessBatchAsync(batchSize: 1);

        _vectorIndexMock.Verify(
            v => v.UpsertBatchAsync(
                It.Is<IReadOnlyList<VectorDocument>>(docs => docs.Any(d => d.DocumentId == $"chunk:{ordered[1].Id}")),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "chunks sharing a CreatedAt timestamp should page by Id instead of being skipped");
    }

    [Fact]
    public async Task ProcessBatchAsync_RevisitsCurrentTimestamp_ForLateInsertedLowerGuidChunk()
    {
        var docId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var processedFirst = new KnowledgeChunk(docId, 0, "first content");
        var lateLowerId = new KnowledgeChunk(docId, 1, "late lower id content");
        var laterTimestamp = new KnowledgeChunk(docId, 2, "later timestamp content");

        SetId(processedFirst, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        SetId(lateLowerId, Guid.Parse("00000000-0000-0000-0000-000000000001"));
        SetCreatedAt(processedFirst, createdAt);
        SetCreatedAt(lateLowerId, createdAt);
        SetCreatedAt(laterTimestamp, createdAt.AddSeconds(1));

        var chunks = new List<KnowledgeChunk> { processedFirst };
        SetupChunkBatch(chunks);
        SetupDocumentLookup(docId, userId);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 0.5f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) =>
                texts.Select(_ => fakeEmbedding).ToList());

        await _sut.ProcessBatchAsync(batchSize: 1);
        chunks.Add(lateLowerId);
        chunks.Add(laterTimestamp);

        var secondResult = await _sut.ProcessBatchAsync(batchSize: 1);

        secondResult.Processed.Should().Be(1);
        _vectorIndexMock.Verify(
            v => v.UpsertBatchAsync(
                It.Is<IReadOnlyList<VectorDocument>>(docs => docs.Any(d => d.DocumentId == $"chunk:{lateLowerId.Id}")),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "forward paging must not skip same-CreatedAt rows inserted after the cursor with lower random GUIDs");
        _vectorIndexMock.Verify(
            v => v.UpsertBatchAsync(
                It.Is<IReadOnlyList<VectorDocument>>(docs => docs.Any(d => d.DocumentId == $"chunk:{laterTimestamp.Id}")),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "the late same-timestamp chunk should be processed before newer timestamps");
    }

    [Fact]
    public async Task ProcessBatchAsync_DefersFailedChunkAndContinuesForwardProgress()
    {
        var docId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var chunks = Enumerable.Range(0, 3)
            .Select(i => new KnowledgeChunk(docId, i, $"content {i}"))
            .ToList();

        for (var i = 0; i < chunks.Count; i++)
        {
            SetCreatedAt(chunks[i], createdAt.AddSeconds(i));
        }

        var ordered = chunks.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToList();
        SetupChunkBatch(chunks);
        SetupDocumentLookup(docId, userId);
        _chunkRepoMock
            .Setup(r => r.GetByIdAsync(ordered[1].Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ordered[1]);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 0.5f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) =>
                texts.Select(_ => fakeEmbedding).ToList());
        _vectorIndexMock
            .Setup(v => v.UpsertBatchAsync(
                It.IsAny<IReadOnlyList<VectorDocument>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("batch upsert failed"));
        _vectorIndexMock
            .Setup(v => v.UpsertAsync(
                $"chunk:{ordered[0].Id}",
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _vectorIndexMock
            .SetupSequence(v => v.UpsertAsync(
                $"chunk:{ordered[1].Id}",
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient index failure"))
            .Returns(Task.CompletedTask);
        _embeddingGeneratorMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var result = await _sut.ProcessBatchAsync(batchSize: 2);
        var forwardResult = await _sut.ProcessBatchAsync(batchSize: 1);
        var retryResult = await _sut.ProcessBatchAsync(batchSize: 1);

        result.Processed.Should().Be(1);
        result.Failed.Should().Be(1);
        forwardResult.Processed.Should().Be(2, "deferred failed chunks should be retried before forward catch-up without starving later chunks");
        retryResult.Processed.Should().Be(0, "the deferred chunk should already have been retried before tail catch-up");
        _vectorIndexMock.Verify(
            v => v.UpsertBatchAsync(
                It.Is<IReadOnlyList<VectorDocument>>(docs => docs.Any(d => d.DocumentId == $"chunk:{ordered[2].Id}")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _vectorIndexMock.Verify(
            v => v.UpsertAsync(
                $"chunk:{ordered[1].Id}",
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "failed chunks should be deferred, then retried after forward pagination can continue");
    }

    [Fact]
    public async Task ProcessBatchAsync_PrunesTrackedStaleVectorsWithBoundedProbeBatch()
    {
        var docId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chunks = Enumerable.Range(0, 510)
            .Select(i => new KnowledgeChunk(docId, i, $"content {i}"))
            .ToList();

        SetupChunkBatch(chunks);
        SetupDocumentLookup(docId, userId);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 0.5f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) =>
                texts.Select(_ => fakeEmbedding).ToList());
        _chunkRepoMock
            .Setup(r => r.GetExistingIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.ToHashSet());

        await _sut.ProcessBatchAsync(batchSize: 510);
        await _sut.ProcessBatchAsync(batchSize: 1);

        _chunkRepoMock.Verify(
            r => r.GetExistingIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count <= 500),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "stale-vector pruning should never send the full tracked ID set when it exceeds the bounded probe size");
    }

    [Fact]
    public async Task ProcessBatchAsync_OperationCanceled_PropagatesException()
    {
        var docId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chunks = new List<KnowledgeChunk>
        {
            new(docId, 0, "content")
        };

        SetupChunkBatch(chunks);

        SetupDocumentLookup(docId, userId);

        _embeddingGeneratorMock
            .Setup(g => g.GenerateBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.ProcessBatchAsync(batchSize: 10));
    }

    [Fact]
    public async Task ProcessBatchAsync_MetadataIncludesUserIdAndDocumentId()
    {
        var docId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var chunk = new KnowledgeChunk(docId, 0, "content for metadata test");

        SetupChunkBatch(new[] { chunk });

        SetupDocumentLookup(docId, userId, boardId);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) =>
                texts.Select(_ => fakeEmbedding).ToList());

        IReadOnlyList<VectorDocument>? capturedDocs = null;
        _vectorIndexMock
            .Setup(v => v.UpsertBatchAsync(
                It.IsAny<IReadOnlyList<VectorDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<VectorDocument>, CancellationToken>(
                (docs, _) => capturedDocs = docs)
            .Returns(Task.CompletedTask);

        await _sut.ProcessBatchAsync(batchSize: 10);

        capturedDocs.Should().NotBeNull();
        capturedDocs.Should().HaveCount(1);
        var capturedMetadata = capturedDocs![0].Metadata;
        capturedMetadata.Should().NotBeNull();
        capturedMetadata!["type"].Should().Be("knowledge_chunk");
        capturedMetadata["documentId"].Should().Be(docId.ToString());
        capturedMetadata["chunkId"].Should().Be(chunk.Id.ToString());
        capturedMetadata["userId"].Should().Be(userId.ToString(),
            "userId must be included for access control");
        capturedMetadata["boardId"].Should().Be(boardId.ToString(),
            "boardId must be included when the document belongs to a board");
    }

    private static void SetCreatedAt(Entity entity, DateTimeOffset createdAt)
    {
        var property = typeof(Entity).GetProperty(nameof(Entity.CreatedAt))
            ?? throw new InvalidOperationException("Expected Entity.CreatedAt property to exist.");
        var setter = property.GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException("Expected Entity.CreatedAt setter to exist.");

        setter.Invoke(entity, [createdAt]);
    }

    private static void SetId(Entity entity, Guid id)
    {
        var property = typeof(Entity).GetProperty(nameof(Entity.Id))
            ?? throw new InvalidOperationException("Expected Entity.Id property to exist.");
        var setter = property.GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException("Expected Entity.Id setter to exist.");

        setter.Invoke(entity, [id]);
    }

    private static List<KnowledgeChunk> GetBatchAfterCursor(
        IReadOnlyList<KnowledgeChunk> chunks,
        KnowledgeChunkBackfillCursor? cursor,
        int batchSize)
    {
        if (cursor is null)
        {
            return chunks
                .OrderBy(c => c.CreatedAt)
                .ThenBy(c => c.Id)
                .Take(batchSize)
                .ToList();
        }

        var processedAtTimestamp = cursor.ProcessedIdsAtCreatedAt ?? new HashSet<Guid>();
        var page = chunks
            .Where(c => c.CreatedAt == cursor.CreatedAt && !processedAtTimestamp.Contains(c.Id))
            .OrderBy(c => c.Id)
            .Take(batchSize)
            .ToList();

        if (page.Count >= batchSize)
            return page;

        page.AddRange(chunks
            .Where(c => c.CreatedAt > cursor.CreatedAt)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Take(batchSize - page.Count));

        return page;
    }

    private static int CountAfterCursor(
        IReadOnlyList<KnowledgeChunk> chunks,
        KnowledgeChunkBackfillCursor? cursor)
    {
        if (cursor is null)
            return chunks.Count;

        var processedAtTimestamp = cursor.ProcessedIdsAtCreatedAt ?? new HashSet<Guid>();
        return chunks.Count(c =>
            (c.CreatedAt == cursor.CreatedAt && !processedAtTimestamp.Contains(c.Id)) ||
            c.CreatedAt > cursor.CreatedAt);
    }
}
