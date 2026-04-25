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
    private readonly InMemoryLogger<EmbeddingBackfillService> _logger;
    private readonly EmbeddingBackfillService _sut;

    public EmbeddingBackfillServiceTests()
    {
        _vectorIndexMock = new Mock<IVectorIndex>();
        _embeddingGeneratorMock = new Mock<IEmbeddingGenerator>();
        _chunkRepoMock = new Mock<IKnowledgeChunkRepository>();
        _logger = new InMemoryLogger<EmbeddingBackfillService>();

        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(true);
        _embeddingGeneratorMock.Setup(g => g.Dimensions).Returns(64);

        _sut = new EmbeddingBackfillService(
            _vectorIndexMock.Object,
            _embeddingGeneratorMock.Object,
            _chunkRepoMock.Object,
            _logger);
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
        var chunks = new List<KnowledgeChunk>
        {
            new(docId, 0, "chunk zero content"),
            new(docId, 1, "chunk one content")
        };

        _chunkRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 0.5f, 0.5f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var result = await _sut.ProcessBatchAsync(batchSize: 10);

        result.Processed.Should().Be(2);
        result.Failed.Should().Be(0);

        _vectorIndexMock.Verify(
            v => v.UpsertAsync(
                It.Is<string>(id => id.StartsWith("chunk:")),
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessBatchAsync_BatchSizeLimitsProcessed()
    {
        var docId = Guid.NewGuid();
        var chunks = Enumerable.Range(0, 10)
            .Select(i => new KnowledgeChunk(docId, i, $"content {i}"))
            .ToList();

        _chunkRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 0.5f, 0.5f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var result = await _sut.ProcessBatchAsync(batchSize: 3);

        result.Processed.Should().Be(3);
        result.Remaining.Should().Be(7);
    }

    [Fact]
    public async Task ProcessBatchAsync_IndividualItemFailure_ContinuesAndReportsFailed()
    {
        var docId = Guid.NewGuid();
        var chunks = new List<KnowledgeChunk>
        {
            new(docId, 0, "good content"),
            new(docId, 1, "bad content"),
            new(docId, 2, "also good content")
        };

        _chunkRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);

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

        result.Processed.Should().Be(2, "two of three chunks should succeed");
        result.Failed.Should().Be(1, "the 'bad content' chunk should fail");
    }

    [Fact]
    public async Task ProcessBatchAsync_OperationCanceled_PropagatesException()
    {
        var docId = Guid.NewGuid();
        var chunks = new List<KnowledgeChunk>
        {
            new(docId, 0, "content")
        };

        _chunkRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);

        _embeddingGeneratorMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.ProcessBatchAsync(batchSize: 10));
    }

    [Fact]
    public async Task ProcessBatchAsync_MetadataIncludesDocumentIdAndChunkId()
    {
        var docId = Guid.NewGuid();
        var chunk = new KnowledgeChunk(docId, 0, "content for metadata test");

        _chunkRepoMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { chunk });

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        IReadOnlyDictionary<string, string>? capturedMetadata = null;
        _vectorIndexMock
            .Setup(v => v.UpsertAsync(
                It.IsAny<string>(),
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ReadOnlyMemory<float>, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (_, _, meta, _) => capturedMetadata = meta)
            .Returns(Task.CompletedTask);

        await _sut.ProcessBatchAsync(batchSize: 10);

        capturedMetadata.Should().NotBeNull();
        capturedMetadata!["type"].Should().Be("knowledge_chunk");
        capturedMetadata["documentId"].Should().Be(docId.ToString());
        capturedMetadata["chunkId"].Should().Be(chunk.Id.ToString());
    }
}
