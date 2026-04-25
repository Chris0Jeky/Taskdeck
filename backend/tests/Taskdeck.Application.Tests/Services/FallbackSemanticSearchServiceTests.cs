using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Services;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class FallbackSemanticSearchServiceTests
{
    private readonly Mock<IVectorIndex> _vectorIndexMock;
    private readonly Mock<IEmbeddingGenerator> _embeddingGeneratorMock;
    private readonly Mock<IKnowledgeSearchService> _ftsSearchMock;
    private readonly InMemoryLogger<FallbackSemanticSearchService> _logger;
    private readonly FallbackSemanticSearchService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();

    public FallbackSemanticSearchServiceTests()
    {
        _vectorIndexMock = new Mock<IVectorIndex>();
        _embeddingGeneratorMock = new Mock<IEmbeddingGenerator>();
        _ftsSearchMock = new Mock<IKnowledgeSearchService>();
        _logger = new InMemoryLogger<FallbackSemanticSearchService>();

        _sut = new FallbackSemanticSearchService(
            _vectorIndexMock.Object,
            _embeddingGeneratorMock.Object,
            _ftsSearchMock.Object,
            _logger);
    }

    #region IsVectorSearchAvailable

    [Fact]
    public void IsVectorSearchAvailable_DelegatesToEmbeddingGenerator()
    {
        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(true);
        _sut.IsVectorSearchAvailable.Should().BeTrue();

        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(false);
        _sut.IsVectorSearchAvailable.Should().BeFalse();
    }

    #endregion

    #region FTS fallback when vector unavailable

    [Fact]
    public async Task SearchAsync_VectorUnavailable_FallsBackToFts()
    {
        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(false);

        var ftsResults = new List<KnowledgeSearchResultDto>
        {
            CreateFtsResult("FTS Result 1")
        };
        _ftsSearchMock
            .Setup(f => f.SearchAsync("test query", _userId, null, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ftsResults);

        var results = await _sut.SearchAsync("test query", _userId);

        results.Should().HaveCount(1);
        _vectorIndexMock.Verify(
            v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "vector index should not be called when embedding generator is unavailable");
    }

    #endregion

    #region Empty/null query

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(true);

        var results = await _sut.SearchAsync("", _userId);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WhitespaceQuery_ReturnsEmpty()
    {
        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(true);

        var results = await _sut.SearchAsync("   ", _userId);

        results.Should().BeEmpty();
    }

    #endregion

    #region Vector search happy path

    [Fact]
    public async Task SearchAsync_VectorAvailable_UsesVectorSearch()
    {
        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f, 0f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateAsync("semantic query", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var docId = Guid.NewGuid();
        var vectorResults = new List<VectorSearchResult>
        {
            new(
                DocumentId: $"chunk:abc",
                Score: 0.95,
                Metadata: new Dictionary<string, string>
                {
                    ["type"] = "knowledge_chunk",
                    ["documentId"] = docId.ToString(),
                    ["chunkId"] = Guid.NewGuid().ToString()
                })
        };

        _vectorIndexMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorResults);

        var results = await _sut.SearchAsync("semantic query", _userId);

        results.Should().HaveCount(1);
        var first = results.First();
        first.DocumentId.Should().Be(docId);
    }

    #endregion

    #region Vector search failure → FTS fallback

    [Fact]
    public async Task SearchAsync_VectorSearchThrows_FallsBackToFts()
    {
        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(true);

        _embeddingGeneratorMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embedding model crashed"));

        var ftsResults = new List<KnowledgeSearchResultDto>
        {
            CreateFtsResult("FTS Fallback Result")
        };
        _ftsSearchMock
            .Setup(f => f.SearchAsync("failing query", _userId, null, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ftsResults);

        var results = await _sut.SearchAsync("failing query", _userId);

        results.Should().HaveCount(1, "should fall back to FTS on vector failure");

        // Verify warning was logged
        _logger.Entries.Should().Contain(e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    #endregion

    #region Vector search returns no results → FTS fallback

    [Fact]
    public async Task SearchAsync_VectorReturnsNoResults_FallsBackToFts()
    {
        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f, 0f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        // Vector index returns results but with no valid documentId in metadata
        var vectorResults = new List<VectorSearchResult>
        {
            new("chunk:abc", 0.9, new Dictionary<string, string>
            {
                ["type"] = "knowledge_chunk",
                ["documentId"] = "not-a-guid"
            })
        };

        _vectorIndexMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorResults);

        var ftsResults = new List<KnowledgeSearchResultDto>
        {
            CreateFtsResult("FTS fallback due to empty vector results")
        };
        _ftsSearchMock
            .Setup(f => f.SearchAsync("test", _userId, null, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ftsResults);

        var results = await _sut.SearchAsync("test", _userId);

        results.Should().HaveCount(1, "should fall back to FTS when vector results are empty after filtering");
    }

    [Fact]
    public async Task SearchAsync_VectorReturnsEmptyList_FallsBackToFts()
    {
        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        _vectorIndexMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());

        var ftsResults = new List<KnowledgeSearchResultDto>
        {
            CreateFtsResult("FTS result")
        };
        _ftsSearchMock
            .Setup(f => f.SearchAsync("empty vector", _userId, null, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ftsResults);

        var results = await _sut.SearchAsync("empty vector", _userId);

        results.Should().HaveCount(1);
    }

    #endregion

    #region Parameters passthrough

    [Fact]
    public async Task SearchAsync_PassesBoardIdAndLimitToFts()
    {
        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(false);

        _ftsSearchMock
            .Setup(f => f.SearchAsync("q", _userId, _boardId, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<KnowledgeSearchResultDto>());

        await _sut.SearchAsync("q", _userId, _boardId, 5);

        _ftsSearchMock.Verify(
            f => f.SearchAsync("q", _userId, _boardId, 5, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    private static KnowledgeSearchResultDto CreateFtsResult(string title)
    {
        return new KnowledgeSearchResultDto(
            DocumentId: Guid.NewGuid(),
            Title: title,
            Snippet: "snippet",
            Rank: 1.0,
            BoardId: null,
            SourceType: KnowledgeSourceType.Manual,
            Tags: null,
            CreatedAt: DateTimeOffset.UtcNow);
    }
}
