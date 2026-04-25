using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Tests.TestUtilities;
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
        _vectorIndexMock = new Mock<IVectorIndex>();
        _embeddingGeneratorMock = new Mock<IEmbeddingGenerator>();
        _chunkRepoMock = new Mock<IKnowledgeChunkRepository>();
        _docRepoMock = new Mock<IKnowledgeDocumentRepository>();
        _logger = new InMemoryLogger<EmbeddingBackfillService>();

        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(true);
        _embeddingGeneratorMock.Setup(g => g.Dimensions).Returns(64);

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
        _chunkRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<KnowledgeChunk>());

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

        _chunkRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);

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

        _chunkRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);

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

        _chunkRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);

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
    public async Task ProcessBatchAsync_OperationCanceled_PropagatesException()
    {
        var docId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chunks = new List<KnowledgeChunk>
        {
            new(docId, 0, "content")
        };

        _chunkRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);

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

        _chunkRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { chunk });

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
}
