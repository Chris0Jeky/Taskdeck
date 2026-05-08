using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class HybridRetrievalServiceTests
{
    private readonly Mock<IFtsKnowledgeSearchService> _ftsMock;
    private readonly Mock<ISemanticSearchService> _semanticMock;
    private readonly Mock<IEmbeddingGenerator> _embeddingMock;
    private readonly Mock<IVectorIndex> _vectorMock;
    private readonly Mock<IKnowledgeDocumentRepository> _docRepoMock;
    private readonly InMemoryLogger<HybridRetrievalService> _logger;
    private readonly HybridRetrievalService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();

    public HybridRetrievalServiceTests()
    {
        _ftsMock = new Mock<IFtsKnowledgeSearchService>();
        _semanticMock = new Mock<ISemanticSearchService>();
        _embeddingMock = new Mock<IEmbeddingGenerator>();
        _vectorMock = new Mock<IVectorIndex>();
        _docRepoMock = new Mock<IKnowledgeDocumentRepository>();
        _logger = new InMemoryLogger<HybridRetrievalService>();

        _sut = new HybridRetrievalService(
            _ftsMock.Object,
            _semanticMock.Object,
            _embeddingMock.Object,
            _vectorMock.Object,
            _docRepoMock.Object,
            _logger);
    }

    #region IsHybridAvailable

    [Fact]
    public void IsHybridAvailable_DelegatesToEmbeddingGenerator()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);
        _sut.IsHybridAvailable.Should().BeTrue();

        _embeddingMock.Setup(g => g.IsAvailable).Returns(false);
        _sut.IsHybridAvailable.Should().BeFalse();
    }

    #endregion

    #region Empty/invalid queries

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        var results = await _sut.SearchAsync("", _userId);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WhitespaceQuery_ReturnsEmpty()
    {
        var results = await _sut.SearchAsync("   ", _userId);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_ZeroLimit_ReturnsEmpty()
    {
        var results = await _sut.SearchAsync("test", _userId, limit: 0);
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_NegativeLimit_ReturnsEmpty()
    {
        var results = await _sut.SearchAsync("test", _userId, limit: -1);
        results.Should().BeEmpty();
    }

    #endregion

    #region FTS-only fallback when vector unavailable

    [Fact]
    public async Task SearchAsync_VectorUnavailable_UsesFtsOnly()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(false);

        var ftsResults = new List<KnowledgeSearchResultDto>
        {
            CreateFtsResult("FTS Result 1", Guid.NewGuid(), 1.0),
            CreateFtsResult("FTS Result 2", Guid.NewGuid(), 0.8)
        };
        _ftsMock
            .Setup(f => f.SearchAsync(
                "test query", _userId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ftsResults);

        var results = await _sut.SearchAsync("test query", _userId);

        results.Should().HaveCount(2);
        results[0].Source.Should().Be(RetrievalSource.Fts);
        results[1].Source.Should().Be(RetrievalSource.Fts);

        _vectorMock.Verify(
            v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_FtsOnly_RespectsLimit()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(false);

        var ftsResults = new List<KnowledgeSearchResultDto>
        {
            CreateFtsResult("R1", Guid.NewGuid(), 1.0),
            CreateFtsResult("R2", Guid.NewGuid(), 0.9),
            CreateFtsResult("R3", Guid.NewGuid(), 0.8)
        };
        _ftsMock
            .Setup(f => f.SearchAsync(
                It.IsAny<string>(), _userId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ftsResults);

        var results = await _sut.SearchAsync("query", _userId, limit: 2);

        results.Should().HaveCount(2);
    }

    #endregion

    #region Hybrid search (RRF fusion)

    [Fact]
    public async Task SearchAsync_BothSourcesReturn_FusesWithRrf()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();
        var docId3 = Guid.NewGuid();

        // FTS returns docs 1, 2
        var ftsResults = new List<KnowledgeSearchResultDto>
        {
            CreateFtsResult("Doc 1", docId1, 1.0),
            CreateFtsResult("Doc 2", docId2, 0.8)
        };
        _ftsMock
            .Setup(f => f.SearchAsync(
                It.IsAny<string>(), _userId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ftsResults);

        // Vector returns docs 2, 3
        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f, 0f });
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var vectorResults = new List<VectorSearchResult>
        {
            new("chunk:2", 0.95, new Dictionary<string, string>
            {
                ["type"] = "knowledge_chunk",
                ["documentId"] = docId2.ToString(),
                ["userId"] = _userId.ToString()
            }),
            new("chunk:3", 0.85, new Dictionary<string, string>
            {
                ["type"] = "knowledge_chunk",
                ["documentId"] = docId3.ToString(),
                ["userId"] = _userId.ToString()
            })
        };
        _vectorMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(vectorResults);

        // Set up document hydration
        SetupDocument(docId1, "Doc 1", "Content 1");
        SetupDocument(docId2, "Doc 2", "Content 2");
        SetupDocument(docId3, "Doc 3", "Content 3");

        var results = await _sut.SearchAsync("query", _userId);

        // Doc 2 should rank highest because it appears in both lists
        results.Should().NotBeEmpty();
        results[0].DocumentId.Should().Be(docId2,
            "document appearing in both FTS and vector results should rank highest via RRF");
        results.Should().OnlyContain(r => r.Source == RetrievalSource.Hybrid);
    }

    [Fact]
    public async Task SearchAsync_OnlyFtsReturns_UsesDirectFtsResults()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var docId = Guid.NewGuid();
        _ftsMock
            .Setup(f => f.SearchAsync(
                It.IsAny<string>(), _userId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeSearchResultDto>
            {
                CreateFtsResult("FTS Only", docId, 1.0)
            });

        _vectorMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());

        var results = await _sut.SearchAsync("query", _userId);

        results.Should().HaveCount(1);
        results[0].Source.Should().Be(RetrievalSource.Fts);
    }

    [Fact]
    public async Task SearchAsync_OnlyVectorReturns_UsesVectorResults()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        _ftsMock
            .Setup(f => f.SearchAsync(
                It.IsAny<string>(), _userId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<KnowledgeSearchResultDto>());

        var docId = Guid.NewGuid();
        _vectorMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>
            {
                new("chunk:1", 0.9, new Dictionary<string, string>
                {
                    ["type"] = "knowledge_chunk",
                    ["documentId"] = docId.ToString(),
                    ["userId"] = _userId.ToString()
                })
            });

        SetupDocument(docId, "Vector Only", "Content");

        var results = await _sut.SearchAsync("query", _userId);

        results.Should().HaveCount(1);
        results[0].Source.Should().Be(RetrievalSource.Vector);
    }

    [Fact]
    public async Task SearchAsync_BothEmpty_ReturnsEmpty()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        _ftsMock
            .Setup(f => f.SearchAsync(
                It.IsAny<string>(), _userId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<KnowledgeSearchResultDto>());

        _vectorMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());

        var results = await _sut.SearchAsync("query", _userId);

        results.Should().BeEmpty();
    }

    #endregion

    #region Error handling and fallback

    [Fact]
    public async Task SearchAsync_VectorSearchThrows_FallsBackToFts()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embedding model crashed"));

        var ftsResults = new List<KnowledgeSearchResultDto>
        {
            CreateFtsResult("FTS Fallback", Guid.NewGuid(), 1.0)
        };
        _ftsMock
            .Setup(f => f.SearchAsync(
                It.IsAny<string>(), _userId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ftsResults);

        var results = await _sut.SearchAsync("query", _userId);

        results.Should().HaveCount(1);
        results[0].Source.Should().Be(RetrievalSource.Fts);
        _logger.Entries.Should().Contain(e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task SearchAsync_OperationCanceled_Propagates()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        _ftsMock
            .Setup(f => f.SearchAsync(
                It.IsAny<string>(), _userId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<KnowledgeSearchResultDto>());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.SearchAsync("query", _userId));
    }

    #endregion

    #region Access control and filtering

    [Fact]
    public async Task SearchAsync_VectorResults_FiltersByBoardId()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        _ftsMock
            .Setup(f => f.SearchAsync(
                It.IsAny<string>(), _userId, _boardId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<KnowledgeSearchResultDto>());

        _vectorMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());

        await _sut.SearchAsync("query", _userId, _boardId);

        _vectorMock.Verify(
            v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.Is<IReadOnlyDictionary<string, string>>(f =>
                    f.ContainsKey("boardId") && f["boardId"] == _boardId.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_VectorResults_ExcludesArchivedDocuments()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var archivedDocId = Guid.NewGuid();
        _ftsMock
            .Setup(f => f.SearchAsync(
                It.IsAny<string>(), _userId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<KnowledgeSearchResultDto>());

        _vectorMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>
            {
                new("chunk:1", 0.9, new Dictionary<string, string>
                {
                    ["documentId"] = archivedDocId.ToString(),
                    ["userId"] = _userId.ToString()
                })
            });

        var archivedDoc = new KnowledgeDocument(
            _userId, "Archived", "Content", KnowledgeSourceType.Manual);
        archivedDoc.Archive();
        _docRepoMock
            .Setup(r => r.GetByIdAsync(archivedDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(archivedDoc);

        var results = await _sut.SearchAsync("query", _userId);

        results.Should().BeEmpty("archived documents should be excluded");
    }

    [Fact]
    public async Task SearchAsync_VectorResults_ExcludesOtherUsersDocuments()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var otherUserDocId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        _ftsMock
            .Setup(f => f.SearchAsync(
                It.IsAny<string>(), _userId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<KnowledgeSearchResultDto>());

        _vectorMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>
            {
                new("chunk:1", 0.9, new Dictionary<string, string>
                {
                    ["documentId"] = otherUserDocId.ToString(),
                    ["userId"] = _userId.ToString()
                })
            });

        var otherDoc = new KnowledgeDocument(
            otherUserId, "Other User", "Content", KnowledgeSourceType.Manual);
        _docRepoMock
            .Setup(r => r.GetByIdAsync(otherUserDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherDoc);

        var results = await _sut.SearchAsync("query", _userId);

        results.Should().BeEmpty("documents owned by other users should be excluded");
    }

    #endregion

    #region RRF algorithm (static)

    [Fact]
    public void ApplyRrf_DocumentInBothLists_HasHigherScore()
    {
        var sharedDocId = Guid.NewGuid();
        var ftsOnlyDocId = Guid.NewGuid();
        var vectorOnlyDocId = Guid.NewGuid();

        var ftsResults = new List<RetrievalResultDto>
        {
            CreateResult("FTS shared", sharedDocId, 1.0, RetrievalSource.Fts),
            CreateResult("FTS only", ftsOnlyDocId, 0.8, RetrievalSource.Fts)
        };

        var vectorResults = new List<RetrievalResultDto>
        {
            CreateResult("Vector shared", sharedDocId, 0.95, RetrievalSource.Vector),
            CreateResult("Vector only", vectorOnlyDocId, 0.85, RetrievalSource.Vector)
        };

        var fused = HybridRetrievalService.ApplyRrf(ftsResults, vectorResults);

        fused.Should().HaveCount(3);
        fused[0].DocumentId.Should().Be(sharedDocId,
            "shared document should rank highest in RRF");
        fused[0].Source.Should().Be(RetrievalSource.Hybrid);
    }

    [Fact]
    public void ApplyRrf_SingleListEmpty_UsesOtherList()
    {
        var docId = Guid.NewGuid();

        var ftsResults = new List<RetrievalResultDto>
        {
            CreateResult("FTS result", docId, 1.0, RetrievalSource.Fts)
        };
        var vectorResults = new List<RetrievalResultDto>();

        var fused = HybridRetrievalService.ApplyRrf(ftsResults, vectorResults);

        fused.Should().HaveCount(1);
        fused[0].DocumentId.Should().Be(docId);
    }

    [Fact]
    public void ApplyRrf_BothEmpty_ReturnsEmpty()
    {
        var fused = HybridRetrievalService.ApplyRrf(
            new List<RetrievalResultDto>(),
            new List<RetrievalResultDto>());

        fused.Should().BeEmpty();
    }

    [Fact]
    public void ApplyRrf_ScoreIsCorrectFormula()
    {
        var docId = Guid.NewGuid();
        var k = HybridRetrievalService.RrfK;

        var ftsResults = new List<RetrievalResultDto>
        {
            CreateResult("Doc", docId, 1.0, RetrievalSource.Fts)
        };
        var vectorResults = new List<RetrievalResultDto>
        {
            CreateResult("Doc", docId, 0.9, RetrievalSource.Vector)
        };

        var fused = HybridRetrievalService.ApplyRrf(ftsResults, vectorResults);

        // Both at rank 1: score = 1/(k+1) + 1/(k+1) = 2/(k+1)
        var expectedScore = 2.0 / (k + 1);
        fused[0].Score.Should().BeApproximately(expectedScore, 1e-10);
    }

    [Fact]
    public void ApplyRrf_MultipleDocuments_OrderByFusedScore()
    {
        var doc1 = Guid.NewGuid();
        var doc2 = Guid.NewGuid();
        var doc3 = Guid.NewGuid();

        // doc1: rank 1 in FTS only
        // doc2: rank 2 in FTS + rank 1 in vector (should be highest)
        // doc3: rank 2 in vector only
        var ftsResults = new List<RetrievalResultDto>
        {
            CreateResult("D1", doc1, 1.0, RetrievalSource.Fts),
            CreateResult("D2", doc2, 0.9, RetrievalSource.Fts)
        };
        var vectorResults = new List<RetrievalResultDto>
        {
            CreateResult("D2", doc2, 0.95, RetrievalSource.Vector),
            CreateResult("D3", doc3, 0.85, RetrievalSource.Vector)
        };

        var fused = HybridRetrievalService.ApplyRrf(ftsResults, vectorResults);

        fused[0].DocumentId.Should().Be(doc2,
            "doc2 appears in both lists and should have the highest RRF score");
    }

    #endregion

    #region BuildEvidenceLinks

    [Fact]
    public void BuildEvidenceLinks_ConvertsRetrievalResults()
    {
        var docId = Guid.NewGuid();
        var results = new List<RetrievalResultDto>
        {
            CreateResult("Test Doc", docId, 0.85, RetrievalSource.Hybrid)
        };

        var evidence = _sut.BuildEvidenceLinks(results);

        evidence.Should().HaveCount(1);
        evidence[0].SourceId.Should().Be(docId);
        evidence[0].SourceType.Should().Be("knowledge_document");
        evidence[0].Label.Should().Be("Test Doc");
        evidence[0].Relevance.Should().BeApproximately(0.85, 0.01);
        evidence[0].Rationale.Should().Contain("hybrid");
    }

    [Fact]
    public void BuildEvidenceLinks_FtsSource_IncludesFtsInRationale()
    {
        var results = new List<RetrievalResultDto>
        {
            CreateResult("Doc", Guid.NewGuid(), 0.5, RetrievalSource.Fts)
        };

        var evidence = _sut.BuildEvidenceLinks(results);

        evidence[0].Rationale.Should().Contain("full-text search");
    }

    [Fact]
    public void BuildEvidenceLinks_VectorSource_IncludesVectorInRationale()
    {
        var results = new List<RetrievalResultDto>
        {
            CreateResult("Doc", Guid.NewGuid(), 0.9, RetrievalSource.Vector)
        };

        var evidence = _sut.BuildEvidenceLinks(results);

        evidence[0].Rationale.Should().Contain("vector similarity");
    }

    [Fact]
    public void BuildEvidenceLinks_ClampsRelevance()
    {
        var results = new List<RetrievalResultDto>
        {
            CreateResult("High", Guid.NewGuid(), 1.5, RetrievalSource.Hybrid),
            CreateResult("Low", Guid.NewGuid(), -0.2, RetrievalSource.Hybrid)
        };

        var evidence = _sut.BuildEvidenceLinks(results);

        evidence[0].Relevance.Should().Be(1.0);
        evidence[1].Relevance.Should().Be(0.0);
    }

    [Fact]
    public void BuildEvidenceLinks_EmptyInput_ReturnsEmpty()
    {
        var evidence = _sut.BuildEvidenceLinks(Array.Empty<RetrievalResultDto>());
        evidence.Should().BeEmpty();
    }

    [Fact]
    public void BuildEvidenceLinks_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => _sut.BuildEvidenceLinks(null!));
    }

    #endregion

    #region Deduplication

    [Fact]
    public async Task SearchAsync_VectorResults_DeduplicatesByDocumentId()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var docId = Guid.NewGuid();
        _ftsMock
            .Setup(f => f.SearchAsync(
                It.IsAny<string>(), _userId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<KnowledgeSearchResultDto>());

        // Two chunks from the same document
        _vectorMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>
            {
                new("chunk:a", 0.95, new Dictionary<string, string>
                {
                    ["documentId"] = docId.ToString(),
                    ["userId"] = _userId.ToString()
                }),
                new("chunk:b", 0.90, new Dictionary<string, string>
                {
                    ["documentId"] = docId.ToString(),
                    ["userId"] = _userId.ToString()
                })
            });

        SetupDocument(docId, "Test Doc", "Content");

        var results = await _sut.SearchAsync("query", _userId);

        results.Should().HaveCount(1, "multiple chunks from same doc should be deduplicated");
    }

    #endregion

    private void SetupDocument(Guid docId, string title, string content)
    {
        var doc = new KnowledgeDocument(
            _userId, title, content, KnowledgeSourceType.Manual);
        _docRepoMock
            .Setup(r => r.GetByIdAsync(docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(doc);
    }

    private static KnowledgeSearchResultDto CreateFtsResult(
        string title, Guid docId, double rank)
    {
        return new KnowledgeSearchResultDto(
            DocumentId: docId,
            Title: title,
            Snippet: "snippet",
            Rank: rank,
            BoardId: null,
            SourceType: KnowledgeSourceType.Manual,
            Tags: null,
            CreatedAt: DateTimeOffset.UtcNow);
    }

    private static RetrievalResultDto CreateResult(
        string title, Guid docId, double score, RetrievalSource source)
    {
        return new RetrievalResultDto(
            DocumentId: docId,
            Title: title,
            Snippet: "snippet",
            Score: score,
            BoardId: null,
            Source: source);
    }
}
