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
    private readonly Mock<IKnowledgeDocumentRepository> _docRepoMock;
    private readonly InMemoryLogger<FallbackSemanticSearchService> _logger;
    private readonly FallbackSemanticSearchService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();

    public FallbackSemanticSearchServiceTests()
    {
        _vectorIndexMock = new Mock<IVectorIndex>();
        _embeddingGeneratorMock = new Mock<IEmbeddingGenerator>();
        _ftsSearchMock = new Mock<IKnowledgeSearchService>();
        _docRepoMock = new Mock<IKnowledgeDocumentRepository>();
        _logger = new InMemoryLogger<FallbackSemanticSearchService>();

        _sut = new FallbackSemanticSearchService(
            _vectorIndexMock.Object,
            _embeddingGeneratorMock.Object,
            _ftsSearchMock.Object,
            _docRepoMock.Object,
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

    #region Vector search happy path with hydration

    [Fact]
    public async Task SearchAsync_VectorAvailable_UsesVectorSearchAndHydratesResults()
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
                    ["chunkId"] = Guid.NewGuid().ToString(),
                    ["userId"] = _userId.ToString()
                })
        };

        _vectorIndexMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorResults);

        // Set up document hydration
        var doc = new KnowledgeDocument(
            _userId, "Hydrated Title", "Hydrated content for the document.",
            KnowledgeSourceType.Manual, null, null, "tag1,tag2");
        _docRepoMock
            .Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        var results = await _sut.SearchAsync("semantic query", _userId);

        var resultList = results.ToList();
        resultList.Should().HaveCount(1);
        resultList[0].DocumentId.Should().Be(docId);
        resultList[0].Title.Should().Be("Hydrated Title",
            "result should be hydrated from the document repository");
        resultList[0].Snippet.Should().Contain("Hydrated content",
            "snippet should contain document content");
        resultList[0].Tags.Should().Be("tag1,tag2");
    }

    [Fact]
    public async Task SearchAsync_VectorSearch_HydratesOverFetchedCandidatesBeforeApplyingLimit()
    {
        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateAsync("semantic query", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var archivedDocId = Guid.NewGuid();
        var validDocId = Guid.NewGuid();
        var vectorResults = new List<VectorSearchResult>
        {
            new("chunk:archived", 0.99, new Dictionary<string, string>
            {
                ["type"] = "knowledge_chunk",
                ["documentId"] = archivedDocId.ToString(),
                ["userId"] = _userId.ToString()
            }),
            new("chunk:valid", 0.98, new Dictionary<string, string>
            {
                ["type"] = "knowledge_chunk",
                ["documentId"] = validDocId.ToString(),
                ["userId"] = _userId.ToString()
            })
        };

        _vectorIndexMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                2,
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorResults);

        var archivedDoc = new KnowledgeDocument(
            _userId, "Archived", "Archived content", KnowledgeSourceType.Manual);
        archivedDoc.Archive();
        var validDoc = new KnowledgeDocument(
            _userId, "Valid", "Valid content", KnowledgeSourceType.Manual);

        _docRepoMock
            .Setup(r => r.GetByIdAsync(archivedDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(archivedDoc);
        _docRepoMock
            .Setup(r => r.GetByIdAsync(validDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validDoc);

        var results = (await _sut.SearchAsync("semantic query", _userId, limit: 1)).ToList();

        results.Should().ContainSingle();
        results[0].DocumentId.Should().Be(validDocId,
            "valid over-fetched candidates should be considered when earlier hits fail hydration");
    }

    [Fact]
    public async Task SearchAsync_VectorSearch_DeduplicatesChunkHitsByDocumentId()
    {
        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateAsync("semantic query", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var firstDocId = Guid.NewGuid();
        var secondDocId = Guid.NewGuid();
        var vectorResults = new List<VectorSearchResult>
        {
            new("chunk:first-a", 0.95, new Dictionary<string, string>
            {
                ["type"] = "knowledge_chunk",
                ["documentId"] = firstDocId.ToString(),
                ["userId"] = _userId.ToString()
            }),
            new("chunk:first-b", 0.90, new Dictionary<string, string>
            {
                ["type"] = "knowledge_chunk",
                ["documentId"] = firstDocId.ToString(),
                ["userId"] = _userId.ToString()
            }),
            new("chunk:second", 0.80, new Dictionary<string, string>
            {
                ["type"] = "knowledge_chunk",
                ["documentId"] = secondDocId.ToString(),
                ["userId"] = _userId.ToString()
            })
        };

        _vectorIndexMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorResults);

        _docRepoMock
            .Setup(r => r.GetByIdAsync(firstDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeDocument(
                _userId, "First", "First content", KnowledgeSourceType.Manual));
        _docRepoMock
            .Setup(r => r.GetByIdAsync(secondDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeDocument(
                _userId, "Second", "Second content", KnowledgeSourceType.Manual));

        var results = (await _sut.SearchAsync("semantic query", _userId)).ToList();

        results.Select(r => r.DocumentId).Should().BeEquivalentTo(new[] { firstDocId, secondDocId });
        results.Should().HaveCount(2, "multiple chunk matches for one document should return one document result");
        _docRepoMock.Verify(
            r => r.GetByIdAsync(firstDocId, It.IsAny<CancellationToken>()),
            Times.Once,
            "lower-ranked duplicate chunk hits should not require repeated hydration");
    }

    #endregion

    #region Access control

    [Fact]
    public async Task SearchAsync_VectorSearch_FiltersQueryByUserId()
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

        _ftsSearchMock
            .Setup(f => f.SearchAsync(It.IsAny<string>(), _userId, null, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<KnowledgeSearchResultDto>());

        await _sut.SearchAsync("test", _userId);

        // Verify the vector index query included userId in the filter
        _vectorIndexMock.Verify(
            v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.Is<IReadOnlyDictionary<string, string>>(f =>
                    f.ContainsKey("userId") && f["userId"] == _userId.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "vector query must include userId filter for access control");
    }

    [Fact]
    public async Task SearchAsync_DocumentBelongsToDifferentUser_ExcludedFromResults()
    {
        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var docId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var vectorResults = new List<VectorSearchResult>
        {
            new("chunk:abc", 0.9, new Dictionary<string, string>
            {
                ["type"] = "knowledge_chunk",
                ["documentId"] = docId.ToString(),
                ["userId"] = _userId.ToString()
            })
        };

        _vectorIndexMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorResults);

        // Document belongs to a different user
        var doc = new KnowledgeDocument(
            otherUserId, "Other User Doc", "Other user content",
            KnowledgeSourceType.Manual);
        _docRepoMock
            .Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        _ftsSearchMock
            .Setup(f => f.SearchAsync(It.IsAny<string>(), _userId, null, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<KnowledgeSearchResultDto>());

        var results = await _sut.SearchAsync("test", _userId);

        results.Should().BeEmpty(
            "documents belonging to other users must be excluded");
    }

    [Fact]
    public async Task SearchAsync_ArchivedDocument_ExcludedFromResults()
    {
        _embeddingGeneratorMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingGeneratorMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var docId = Guid.NewGuid();
        var vectorResults = new List<VectorSearchResult>
        {
            new("chunk:abc", 0.9, new Dictionary<string, string>
            {
                ["type"] = "knowledge_chunk",
                ["documentId"] = docId.ToString(),
                ["userId"] = _userId.ToString()
            })
        };

        _vectorIndexMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorResults);

        // Document is archived
        var doc = new KnowledgeDocument(
            _userId, "Archived Doc", "Archived content",
            KnowledgeSourceType.Manual);
        doc.Archive();
        _docRepoMock
            .Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);

        _ftsSearchMock
            .Setup(f => f.SearchAsync(It.IsAny<string>(), _userId, null, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<KnowledgeSearchResultDto>());

        var results = await _sut.SearchAsync("test", _userId);

        results.Should().BeEmpty(
            "archived documents must be excluded from search results");
    }

    #endregion

    #region Vector search failure -> FTS fallback

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

    #region Vector search returns no results -> FTS fallback

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
