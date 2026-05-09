using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Xunit;
using static Taskdeck.Application.Services.RetrievalEvalService;

namespace Taskdeck.Application.Tests.Services;

public class RetrievalEvalServiceTests
{
    private readonly Guid _userId = Guid.NewGuid();

    #region EvaluateAsync

    [Fact]
    public async Task EvaluateAsync_EmptyCases_ReturnsZeroReport()
    {
        var service = new Mock<IHybridRetrievalService>();

        var report = await RetrievalEvalService.EvaluateAsync(
            service.Object,
            Array.Empty<EvalCase>(),
            _userId);

        report.TotalCases.Should().Be(0);
        report.MeanRecallAtK.Should().Be(0.0);
        report.MeanPrecisionAtK.Should().Be(0.0);
    }

    [Fact]
    public async Task EvaluateAsync_PerfectRetrieval_ReturnsOne()
    {
        var relevantDocId = Guid.NewGuid();
        var service = new Mock<IHybridRetrievalService>();
        service
            .Setup(s => s.SearchAsync(
                "test query", _userId, null, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalResultDto>
            {
                new(relevantDocId, "Relevant", "snippet", 0.9, null, RetrievalSource.Hybrid)
            });

        var cases = new List<EvalCase>
        {
            new("test query", new HashSet<Guid> { relevantDocId })
        };

        var report = await RetrievalEvalService.EvaluateAsync(
            service.Object, cases, _userId);

        report.TotalCases.Should().Be(1);
        report.MeanRecallAtK.Should().Be(1.0);
        report.MeanPrecisionAtK.Should().Be(1.0);
        report.CaseResults[0].RelevantRetrievedCount.Should().Be(1);
    }

    [Fact]
    public async Task EvaluateAsync_NoRelevantRetrieved_ReturnsZeroRecall()
    {
        var relevantDocId = Guid.NewGuid();
        var irrelevantDocId = Guid.NewGuid();

        var service = new Mock<IHybridRetrievalService>();
        service
            .Setup(s => s.SearchAsync(
                "test query", _userId, null, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalResultDto>
            {
                new(irrelevantDocId, "Irrelevant", "snippet", 0.5, null, RetrievalSource.Fts)
            });

        var cases = new List<EvalCase>
        {
            new("test query", new HashSet<Guid> { relevantDocId })
        };

        var report = await RetrievalEvalService.EvaluateAsync(
            service.Object, cases, _userId);

        report.MeanRecallAtK.Should().Be(0.0);
        report.MeanPrecisionAtK.Should().Be(0.0);
    }

    [Fact]
    public async Task EvaluateAsync_PartialRecall_ComputesCorrectly()
    {
        var doc1 = Guid.NewGuid();
        var doc2 = Guid.NewGuid();
        var doc3 = Guid.NewGuid();

        var service = new Mock<IHybridRetrievalService>();
        service
            .Setup(s => s.SearchAsync(
                "test query", _userId, null, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalResultDto>
            {
                new(doc1, "Doc 1", "s", 0.9, null, RetrievalSource.Hybrid),
                new(doc3, "Doc 3", "s", 0.5, null, RetrievalSource.Hybrid)
            });

        // 2 relevant, but only 1 retrieved
        var cases = new List<EvalCase>
        {
            new("test query", new HashSet<Guid> { doc1, doc2 })
        };

        var report = await RetrievalEvalService.EvaluateAsync(
            service.Object, cases, _userId);

        // recall@10 = 1/2 = 0.5 (found doc1 out of {doc1, doc2})
        report.CaseResults[0].RecallAtK.Should().BeApproximately(0.5, 0.01);
        // precision@10 = 1/2 = 0.5 (1 relevant out of 2 retrieved)
        report.CaseResults[0].PrecisionAtK.Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public async Task EvaluateAsync_MultipleCases_AveragesCorrectly()
    {
        var doc1 = Guid.NewGuid();
        var doc2 = Guid.NewGuid();

        var service = new Mock<IHybridRetrievalService>();

        // Case 1: perfect retrieval
        service
            .Setup(s => s.SearchAsync(
                "query1", _userId, null, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalResultDto>
            {
                new(doc1, "Doc 1", "s", 0.9, null, RetrievalSource.Hybrid)
            });

        // Case 2: no relevant retrieved
        service
            .Setup(s => s.SearchAsync(
                "query2", _userId, null, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalResultDto>());

        var cases = new List<EvalCase>
        {
            new("query1", new HashSet<Guid> { doc1 }),
            new("query2", new HashSet<Guid> { doc2 })
        };

        var report = await RetrievalEvalService.EvaluateAsync(
            service.Object, cases, _userId);

        // Mean recall: (1.0 + 0.0) / 2 = 0.5
        report.MeanRecallAtK.Should().BeApproximately(0.5, 0.01);
        report.TotalCases.Should().Be(2);
    }

    [Fact]
    public async Task EvaluateAsync_NoRelevantDocs_RecallIsTriviallyOne()
    {
        var service = new Mock<IHybridRetrievalService>();
        service
            .Setup(s => s.SearchAsync(
                "query", _userId, null, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalResultDto>());

        var cases = new List<EvalCase>
        {
            new("query", new HashSet<Guid>()) // No relevant docs
        };

        var report = await RetrievalEvalService.EvaluateAsync(
            service.Object, cases, _userId);

        report.CaseResults[0].RecallAtK.Should().Be(1.0,
            "when there are no relevant docs, recall is trivially 1.0");
    }

    [Fact]
    public async Task EvaluateAsync_ZeroK_ThrowsArgumentOutOfRange()
    {
        var service = new Mock<IHybridRetrievalService>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => RetrievalEvalService.EvaluateAsync(
                service.Object,
                new List<EvalCase>(),
                _userId,
                k: 0));
    }

    [Fact]
    public async Task EvaluateAsync_NullService_ThrowsArgumentNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => RetrievalEvalService.EvaluateAsync(
                null!,
                new List<EvalCase>(),
                _userId));
    }

    [Fact]
    public async Task EvaluateAsync_NullCases_ThrowsArgumentNull()
    {
        var service = new Mock<IHybridRetrievalService>();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => RetrievalEvalService.EvaluateAsync(
                service.Object,
                null!,
                _userId));
    }

    #endregion

    #region EvaluateDuplicateDetectionAsync

    [Fact]
    public async Task EvaluateDuplicateDetection_EmptyCases_ReturnsZeroReport()
    {
        var service = new Mock<IDuplicateDetectionService>();

        var report = await RetrievalEvalService.EvaluateDuplicateDetectionAsync(
            service.Object,
            Array.Empty<DuplicateEvalCase>(),
            _userId);

        report.TotalCases.Should().Be(0);
    }

    [Fact]
    public async Task EvaluateDuplicateDetection_PerfectDetection_ReturnsFullPrecisionAndRecall()
    {
        var service = new Mock<IDuplicateDetectionService>();
        service
            .Setup(s => s.DetectAsync(
                "dup content", "dup title", _userId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DuplicateDetectionResultDto(
                true, 0.95, Guid.NewGuid(), "Existing", "similar to existing: Existing"));

        service
            .Setup(s => s.DetectAsync(
                "unique content", "unique title", _userId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DuplicateDetectionResultDto(false, 0.0, null, null, null));

        var cases = new List<DuplicateEvalCase>
        {
            new("dup content", "dup title", true),
            new("unique content", "unique title", false)
        };

        var report = await RetrievalEvalService.EvaluateDuplicateDetectionAsync(
            service.Object, cases, _userId);

        report.Precision.Should().Be(1.0);
        report.Recall.Should().Be(1.0);
        report.TruePositives.Should().Be(1);
        report.TrueNegatives.Should().Be(1);
        report.FalsePositives.Should().Be(0);
        report.FalseNegatives.Should().Be(0);
    }

    [Fact]
    public async Task EvaluateDuplicateDetection_FalsePositive_ReducesPrecision()
    {
        var service = new Mock<IDuplicateDetectionService>();

        // Always flags as duplicate, even for non-duplicates
        service
            .Setup(s => s.DetectAsync(
                It.IsAny<string>(), It.IsAny<string>(), _userId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DuplicateDetectionResultDto(
                true, 0.95, Guid.NewGuid(), "Existing", "similar to existing"));

        var cases = new List<DuplicateEvalCase>
        {
            new("dup", "dup", true, "true duplicate"),
            new("unique", "unique", false, "false positive")
        };

        var report = await RetrievalEvalService.EvaluateDuplicateDetectionAsync(
            service.Object, cases, _userId);

        report.Precision.Should().BeApproximately(0.5, 0.01,
            "1 TP + 1 FP = precision 0.5");
        report.Recall.Should().Be(1.0);
        report.FalsePositives.Should().Be(1);
    }

    [Fact]
    public async Task EvaluateDuplicateDetection_FalseNegative_ReducesRecall()
    {
        var service = new Mock<IDuplicateDetectionService>();

        // Never flags as duplicate
        service
            .Setup(s => s.DetectAsync(
                It.IsAny<string>(), It.IsAny<string>(), _userId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DuplicateDetectionResultDto(false, 0.0, null, null, null));

        var cases = new List<DuplicateEvalCase>
        {
            new("dup", "dup", true, "missed duplicate"),
            new("unique", "unique", false, "correct negative")
        };

        var report = await RetrievalEvalService.EvaluateDuplicateDetectionAsync(
            service.Object, cases, _userId);

        report.Recall.Should().Be(0.0,
            "missed all true duplicates = zero recall");
        report.FalseNegatives.Should().Be(1);
        report.TrueNegatives.Should().Be(1);
    }

    #endregion

    #region Hand-labeled holdout fixture

    /// <summary>
    /// Demonstrates the eval framework with a hand-labeled holdout set.
    /// In a real deployment, these would be seeded into the database and
    /// run against the actual retrieval pipeline.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_HandLabeledHoldout_MeasuresRecallAtTen()
    {
        var apiReviewDocId = Guid.NewGuid();
        var authDesignDocId = Guid.NewGuid();
        var boardBugDocId = Guid.NewGuid();
        var deployGuideDocId = Guid.NewGuid();
        var perfBudgetDocId = Guid.NewGuid();

        var service = new Mock<IHybridRetrievalService>();

        // Query 1: "API review process" -> relevant: apiReviewDocId
        service
            .Setup(s => s.SearchAsync(
                "API review process", _userId, null, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalResultDto>
            {
                new(apiReviewDocId, "API Review Notes", "s", 0.9, null, RetrievalSource.Hybrid),
                new(Guid.NewGuid(), "Unrelated", "s", 0.3, null, RetrievalSource.Fts)
            });

        // Query 2: "authentication design" -> relevant: authDesignDocId
        service
            .Setup(s => s.SearchAsync(
                "authentication design", _userId, null, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalResultDto>
            {
                new(authDesignDocId, "Auth Design", "s", 0.85, null, RetrievalSource.Hybrid)
            });

        // Query 3: "board drag bug" -> relevant: boardBugDocId
        service
            .Setup(s => s.SearchAsync(
                "board drag bug", _userId, null, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalResultDto>
            {
                new(boardBugDocId, "Board Bug Report", "s", 0.8, null, RetrievalSource.Hybrid)
            });

        // Query 4: "deployment guide" -> relevant: deployGuideDocId
        service
            .Setup(s => s.SearchAsync(
                "deployment guide", _userId, null, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalResultDto>
            {
                new(Guid.NewGuid(), "Wrong doc", "s", 0.6, null, RetrievalSource.Fts)
            });

        // Query 5: "performance budgets" -> relevant: perfBudgetDocId
        service
            .Setup(s => s.SearchAsync(
                "performance budgets", _userId, null, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievalResultDto>
            {
                new(perfBudgetDocId, "Perf Budgets", "s", 0.7, null, RetrievalSource.Hybrid),
                new(Guid.NewGuid(), "Noise", "s", 0.2, null, RetrievalSource.Fts)
            });

        var holdout = new List<EvalCase>
        {
            new("API review process",
                new HashSet<Guid> { apiReviewDocId },
                "API review documentation retrieval"),
            new("authentication design",
                new HashSet<Guid> { authDesignDocId },
                "Auth design documentation retrieval"),
            new("board drag bug",
                new HashSet<Guid> { boardBugDocId },
                "Bug report retrieval"),
            new("deployment guide",
                new HashSet<Guid> { deployGuideDocId },
                "Deploy guide retrieval (expected miss)"),
            new("performance budgets",
                new HashSet<Guid> { perfBudgetDocId },
                "Perf budgets retrieval")
        };

        var report = await RetrievalEvalService.EvaluateAsync(
            service.Object, holdout, _userId, k: 10);

        report.TotalCases.Should().Be(5);
        // 4 out of 5 queries found the relevant doc
        report.MeanRecallAtK.Should().BeApproximately(0.8, 0.01);

        // Each case result should be tracked
        report.CaseResults.Should().HaveCount(5);
        report.CaseResults[3].RecallAtK.Should().Be(0.0,
            "deployment guide was not retrieved");
    }

    #endregion
}
