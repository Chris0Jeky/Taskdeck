using System.Reflection;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class DuplicateDetectionServiceTests
{
    private readonly Mock<IEmbeddingGenerator> _embeddingMock;
    private readonly Mock<IVectorIndex> _vectorMock;
    private readonly Mock<IKnowledgeDocumentRepository> _docRepoMock;
    private readonly Mock<IFtsKnowledgeSearchService> _ftsMock;
    private readonly InMemoryLogger<DuplicateDetectionService> _logger;
    private readonly DuplicateDetectionService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();

    /// <summary>
    /// Documents registered via <see cref="RegisterDocWithId"/> for batch-fetch mock.
    /// </summary>
    private readonly Dictionary<Guid, KnowledgeDocument> _registeredDocs = new();

    public DuplicateDetectionServiceTests()
    {
        _embeddingMock = new Mock<IEmbeddingGenerator>();
        _vectorMock = new Mock<IVectorIndex>();
        _docRepoMock = new Mock<IKnowledgeDocumentRepository>();
        _ftsMock = new Mock<IFtsKnowledgeSearchService>();
        _logger = new InMemoryLogger<DuplicateDetectionService>();

        // Default setup: GetByIdsAsync returns matching registered documents
        _docRepoMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Guid> ids, CancellationToken _) =>
                ids.Where(id => _registeredDocs.ContainsKey(id))
                   .Select(id => _registeredDocs[id])
                   .ToList());

        _sut = new DuplicateDetectionService(
            _embeddingMock.Object,
            _vectorMock.Object,
            _docRepoMock.Object,
            _ftsMock.Object,
            _logger);
    }

    private void RegisterDocWithId(Guid docId, KnowledgeDocument doc)
    {
        var idProp = typeof(KnowledgeDocument).BaseType!
            .GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!;
        idProp.SetValue(doc, docId);
        _registeredDocs[docId] = doc;
    }

    #region Embedding unavailable -- FTS title fallback

    [Fact]
    public async Task DetectAsync_EmbeddingUnavailable_NoFtsMatches_ReturnsNoDuplicate()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(false);
        _ftsMock
            .Setup(f => f.SearchAsync(
                It.IsAny<string>(), _userId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<KnowledgeSearchResultDto>());

        var result = await _sut.DetectAsync("content", "title", _userId);

        result.IsProbableDuplicate.Should().BeFalse();
        result.SimilarityScore.Should().Be(0.0);
        result.MatchedDocumentId.Should().BeNull();
        result.ReviewCue.Should().BeNull();
    }

    [Fact]
    public async Task DetectAsync_EmbeddingUnavailable_ExactTitleMatch_FlagsDuplicate()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(false);

        var existingDocId = Guid.NewGuid();
        _ftsMock
            .Setup(f => f.SearchAsync(
                It.IsAny<string>(), _userId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeSearchResultDto>
            {
                new(DocumentId: existingDocId, Title: "My Document Title", Snippet: "snippet",
                    Rank: 1.0, BoardId: null, SourceType: KnowledgeSourceType.Manual,
                    Tags: null, CreatedAt: DateTimeOffset.UtcNow)
            });

        var result = await _sut.DetectAsync("content", "My Document Title", _userId);

        result.IsProbableDuplicate.Should().BeTrue(
            "exact title match should flag as probable duplicate via FTS fallback");
        result.SimilarityScore.Should().Be(1.0);
        result.MatchedDocumentId.Should().Be(existingDocId);
    }

    [Fact]
    public async Task DetectAsync_EmbeddingUnavailable_DissimilarTitle_ReturnsNoDuplicate()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(false);

        var existingDocId = Guid.NewGuid();
        _ftsMock
            .Setup(f => f.SearchAsync(
                It.IsAny<string>(), _userId, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeSearchResultDto>
            {
                new(DocumentId: existingDocId, Title: "Completely Different Title", Snippet: "snippet",
                    Rank: 1.0, BoardId: null, SourceType: KnowledgeSourceType.Manual,
                    Tags: null, CreatedAt: DateTimeOffset.UtcNow)
            });

        var result = await _sut.DetectAsync("content", "My Document Title", _userId);

        result.IsProbableDuplicate.Should().BeFalse(
            "dissimilar titles should not flag as duplicate");
    }

    #endregion

    #region Empty content

    [Fact]
    public async Task DetectAsync_EmptyContent_ReturnsNoDuplicate()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var result = await _sut.DetectAsync("", "title", _userId);

        result.IsProbableDuplicate.Should().BeFalse();
    }

    [Fact]
    public async Task DetectAsync_WhitespaceContent_ReturnsNoDuplicate()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var result = await _sut.DetectAsync("   ", "title", _userId);

        result.IsProbableDuplicate.Should().BeFalse();
    }

    #endregion

    #region No candidates

    [Fact]
    public async Task DetectAsync_NoCandidates_ReturnsNoDuplicate()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        _vectorMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());

        var result = await _sut.DetectAsync("new content", "title", _userId);

        result.IsProbableDuplicate.Should().BeFalse();
        result.MatchedDocumentId.Should().BeNull();
    }

    #endregion

    #region Hard threshold (probable duplicate)

    [Fact]
    public async Task DetectAsync_ScoreAboveHardThreshold_FlagsProbableDuplicate()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var existingDocId = Guid.NewGuid();
        _vectorMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>
            {
                new("chunk:1", 0.95, new Dictionary<string, string>
                {
                    ["documentId"] = existingDocId.ToString(),
                    ["userId"] = _userId.ToString()
                })
            });

        var existingDoc = new KnowledgeDocument(
            _userId, "Existing Doc", "Very similar content", KnowledgeSourceType.Manual);
        RegisterDocWithId(existingDocId, existingDoc);

        var result = await _sut.DetectAsync("new content", "title", _userId);

        result.IsProbableDuplicate.Should().BeTrue();
        result.SimilarityScore.Should().BeGreaterOrEqualTo(DuplicateDetectionService.HardThreshold);
        result.MatchedDocumentId.Should().Be(existingDocId);
        result.MatchedDocumentTitle.Should().Be("Existing Doc");
        result.ReviewCue.Should().Contain("similar to existing");
        result.ReviewCue.Should().Contain("Existing Doc");
    }

    #endregion

    #region Soft threshold (review cue only)

    [Fact]
    public async Task DetectAsync_ScoreBetweenSoftAndHard_SurfacesReviewCueWithoutFlagging()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var existingDocId = Guid.NewGuid();
        _vectorMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>
            {
                new("chunk:1", 0.85, new Dictionary<string, string>
                {
                    ["documentId"] = existingDocId.ToString(),
                    ["userId"] = _userId.ToString()
                })
            });

        var existingDoc = new KnowledgeDocument(
            _userId, "Similar Doc", "Somewhat similar", KnowledgeSourceType.Manual);
        RegisterDocWithId(existingDocId, existingDoc);

        var result = await _sut.DetectAsync("new content", "title", _userId);

        result.IsProbableDuplicate.Should().BeFalse(
            "score between soft and hard threshold should not flag as probable duplicate");
        result.SimilarityScore.Should().BeGreaterOrEqualTo(DuplicateDetectionService.SoftThreshold);
        result.MatchedDocumentId.Should().Be(existingDocId);
        result.ReviewCue.Should().Contain("similar to existing");
    }

    #endregion

    #region Below soft threshold

    [Fact]
    public async Task DetectAsync_ScoreBelowSoftThreshold_ReturnsNoDuplicate()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var existingDocId = Guid.NewGuid();
        _vectorMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>
            {
                new("chunk:1", 0.5, new Dictionary<string, string>
                {
                    ["documentId"] = existingDocId.ToString(),
                    ["userId"] = _userId.ToString()
                })
            });

        var existingDoc = new KnowledgeDocument(
            _userId, "Different Doc", "Different content", KnowledgeSourceType.Manual);
        RegisterDocWithId(existingDocId, existingDoc);

        var result = await _sut.DetectAsync("new content", "title", _userId);

        result.IsProbableDuplicate.Should().BeFalse();
        result.MatchedDocumentId.Should().BeNull();
        result.ReviewCue.Should().BeNull();
    }

    #endregion

    #region Exclude self

    [Fact]
    public async Task DetectAsync_ExcludesOwnDocument_WhenExcludeIdProvided()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var selfDocId = Guid.NewGuid();
        var otherDocId = Guid.NewGuid();

        _vectorMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>
            {
                // Self-match at highest score
                new("chunk:self", 0.99, new Dictionary<string, string>
                {
                    ["documentId"] = selfDocId.ToString(),
                    ["userId"] = _userId.ToString()
                }),
                // Other match below soft threshold
                new("chunk:other", 0.5, new Dictionary<string, string>
                {
                    ["documentId"] = otherDocId.ToString(),
                    ["userId"] = _userId.ToString()
                })
            });

        var otherDoc = new KnowledgeDocument(
            _userId, "Other", "Content", KnowledgeSourceType.Manual);
        RegisterDocWithId(otherDocId, otherDoc);

        var result = await _sut.DetectAsync(
            "content", "title", _userId, excludeDocumentId: selfDocId);

        result.MatchedDocumentId.Should().NotBe(selfDocId,
            "self-match should be excluded when excludeDocumentId is set");
    }

    #endregion

    #region Access control

    [Fact]
    public async Task DetectAsync_SkipsArchivedDocuments()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var archivedDocId = Guid.NewGuid();
        _vectorMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>
            {
                new("chunk:1", 0.95, new Dictionary<string, string>
                {
                    ["documentId"] = archivedDocId.ToString(),
                    ["userId"] = _userId.ToString()
                })
            });

        var archivedDoc = new KnowledgeDocument(
            _userId, "Archived", "Content", KnowledgeSourceType.Manual);
        archivedDoc.Archive();
        RegisterDocWithId(archivedDocId, archivedDoc);

        var result = await _sut.DetectAsync("content", "title", _userId);

        result.IsProbableDuplicate.Should().BeFalse(
            "archived documents should not be considered for duplicate detection");
    }

    [Fact]
    public async Task DetectAsync_SkipsOtherUsersDocuments()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var otherUserDocId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        _vectorMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>
            {
                new("chunk:1", 0.95, new Dictionary<string, string>
                {
                    ["documentId"] = otherUserDocId.ToString(),
                    ["userId"] = _userId.ToString()
                })
            });

        var otherDoc = new KnowledgeDocument(
            otherUserId, "Other's Doc", "Content", KnowledgeSourceType.Manual);
        RegisterDocWithId(otherUserDocId, otherDoc);

        var result = await _sut.DetectAsync("content", "title", _userId);

        result.IsProbableDuplicate.Should().BeFalse(
            "documents belonging to other users should not trigger duplicate detection");
    }

    #endregion

    #region Board scoping

    [Fact]
    public async Task DetectAsync_WithBoardId_IncludesBoardInFilter()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);

        var fakeEmbedding = new ReadOnlyMemory<float>(new float[] { 1f });
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        _vectorMock
            .Setup(v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VectorSearchResult>());

        await _sut.DetectAsync("content", "title", _userId, _boardId);

        _vectorMock.Verify(
            v => v.QueryAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.Is<IReadOnlyDictionary<string, string>>(f =>
                    f.ContainsKey("boardId") && f["boardId"] == _boardId.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Error handling

    [Fact]
    public async Task DetectAsync_EmbeddingGeneratorThrows_ReturnsSafeNoDuplicate()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("model crashed"));

        var result = await _sut.DetectAsync("content", "title", _userId);

        result.IsProbableDuplicate.Should().BeFalse(
            "failures should return safe no-duplicate result");
        _logger.Entries.Should().Contain(e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task DetectAsync_OperationCanceled_Propagates()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(true);
        _embeddingMock
            .Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.DetectAsync("content", "title", _userId));
    }

    #endregion

    #region Threshold calibration

    [Fact]
    public void ThresholdConstants_ArePrecisionFavoring()
    {
        DuplicateDetectionService.HardThreshold.Should().BeGreaterOrEqualTo(0.90,
            "hard threshold should be high to favor precision over recall");
        DuplicateDetectionService.SoftThreshold.Should().BeLessThan(DuplicateDetectionService.HardThreshold,
            "soft threshold should be lower than hard threshold");
        DuplicateDetectionService.SoftThreshold.Should().BeGreaterOrEqualTo(0.70,
            "soft threshold should not be so low that it generates excessive review noise");
    }

    #endregion
}
