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
    private readonly InMemoryLogger<DuplicateDetectionService> _logger;
    private readonly DuplicateDetectionService _sut;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _boardId = Guid.NewGuid();

    public DuplicateDetectionServiceTests()
    {
        _embeddingMock = new Mock<IEmbeddingGenerator>();
        _vectorMock = new Mock<IVectorIndex>();
        _docRepoMock = new Mock<IKnowledgeDocumentRepository>();
        _logger = new InMemoryLogger<DuplicateDetectionService>();

        _sut = new DuplicateDetectionService(
            _embeddingMock.Object,
            _vectorMock.Object,
            _docRepoMock.Object,
            _logger);
    }

    #region Embedding unavailable

    [Fact]
    public async Task DetectAsync_EmbeddingUnavailable_ReturnsNoDuplicate()
    {
        _embeddingMock.Setup(g => g.IsAvailable).Returns(false);

        var result = await _sut.DetectAsync("content", "title", _userId);

        result.IsProbableDuplicate.Should().BeFalse();
        result.SimilarityScore.Should().Be(0.0);
        result.MatchedDocumentId.Should().BeNull();
        result.ReviewCue.Should().BeNull();
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
        _docRepoMock
            .Setup(r => r.GetByIdAsync(existingDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDoc);

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
        _docRepoMock
            .Setup(r => r.GetByIdAsync(existingDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDoc);

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
        _docRepoMock
            .Setup(r => r.GetByIdAsync(existingDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDoc);

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
        _docRepoMock
            .Setup(r => r.GetByIdAsync(otherDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherDoc);

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
        _docRepoMock
            .Setup(r => r.GetByIdAsync(archivedDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(archivedDoc);

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
        _docRepoMock
            .Setup(r => r.GetByIdAsync(otherUserDocId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherDoc);

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
