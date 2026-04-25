using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Infrastructure.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class InMemoryVectorIndexTests
{
    private readonly InMemoryVectorIndex _sut = new();

    #region UpsertAsync

    [Fact]
    public async Task UpsertAsync_SingleVector_StoresAndIncrementsCount()
    {
        var vector = new float[] { 1f, 0f, 0f };

        await _sut.UpsertAsync("doc-1", vector);

        var count = await _sut.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task UpsertAsync_DuplicateId_ReplacesExistingVector()
    {
        var original = new float[] { 1f, 0f, 0f };
        var replacement = new float[] { 0f, 1f, 0f };

        await _sut.UpsertAsync("doc-1", original);
        await _sut.UpsertAsync("doc-1", replacement);

        var count = await _sut.CountAsync();
        count.Should().Be(1, "duplicate ID should replace, not add");

        // Query with the replacement vector should match with high similarity
        var results = await _sut.QueryAsync(replacement, topK: 1);
        results.Should().HaveCount(1);
        results[0].DocumentId.Should().Be("doc-1");
        results[0].Score.Should().BeApproximately(1.0, 0.001,
            "querying with the exact replacement vector should yield perfect similarity");
    }

    [Fact]
    public async Task UpsertAsync_NullId_ThrowsArgumentNullException()
    {
        var vector = new float[] { 1f, 0f };

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.UpsertAsync(null!, vector));
    }

    [Fact]
    public async Task UpsertAsync_EmptyId_ThrowsArgumentException()
    {
        var vector = new float[] { 1f, 0f };

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => _sut.UpsertAsync("", vector));
    }

    [Fact]
    public async Task UpsertAsync_WhitespaceId_ThrowsArgumentException()
    {
        var vector = new float[] { 1f, 0f };

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => _sut.UpsertAsync("   ", vector));
    }

    [Fact]
    public async Task UpsertAsync_WithMetadata_PreservesMetadata()
    {
        var vector = new float[] { 1f, 0f, 0f };
        var metadata = new Dictionary<string, string>
        {
            ["type"] = "chunk",
            ["boardId"] = "abc-123"
        };

        await _sut.UpsertAsync("doc-1", vector, metadata);

        var results = await _sut.QueryAsync(vector, topK: 1);
        results[0].Metadata.Should().NotBeNull();
        results[0].Metadata!["type"].Should().Be("chunk");
        results[0].Metadata!["boardId"].Should().Be("abc-123");
    }

    #endregion

    #region UpsertBatchAsync

    [Fact]
    public async Task UpsertBatchAsync_MultipleDocuments_AllStored()
    {
        var docs = new List<VectorDocument>
        {
            new("doc-1", new float[] { 1f, 0f, 0f }),
            new("doc-2", new float[] { 0f, 1f, 0f }),
            new("doc-3", new float[] { 0f, 0f, 1f })
        };

        await _sut.UpsertBatchAsync(docs);

        var count = await _sut.CountAsync();
        count.Should().Be(3);
    }

    [Fact]
    public async Task UpsertBatchAsync_NullInput_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.UpsertBatchAsync(null!));
    }

    [Fact]
    public async Task UpsertBatchAsync_CancellationDuringBatch_ThrowsOperationCanceled()
    {
        var docs = Enumerable.Range(0, 1000)
            .Select(i => new VectorDocument($"doc-{i}", new float[] { 1f, 0f }))
            .ToList();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.UpsertBatchAsync(docs, cts.Token));
    }

    #endregion

    #region QueryAsync

    [Fact]
    public async Task QueryAsync_EmptyIndex_ReturnsEmptyList()
    {
        var query = new float[] { 1f, 0f, 0f };

        var results = await _sut.QueryAsync(query, topK: 5);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_TopKZeroOrNegative_ReturnsEmptyList()
    {
        await _sut.UpsertAsync("doc-1", new float[] { 1f, 0f });

        var resultsZero = await _sut.QueryAsync(new float[] { 1f, 0f }, topK: 0);
        resultsZero.Should().BeEmpty();

        var resultsNeg = await _sut.QueryAsync(new float[] { 1f, 0f }, topK: -1);
        resultsNeg.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_NearestNeighborAccuracy_ReturnsClosestFirst()
    {
        // Three orthogonal unit vectors
        await _sut.UpsertAsync("x-axis", new float[] { 1f, 0f, 0f });
        await _sut.UpsertAsync("y-axis", new float[] { 0f, 1f, 0f });
        await _sut.UpsertAsync("z-axis", new float[] { 0f, 0f, 1f });

        // Query close to x-axis
        var query = new float[] { 0.9f, 0.1f, 0f };
        var results = await _sut.QueryAsync(query, topK: 3);

        results.Should().HaveCount(3);
        results[0].DocumentId.Should().Be("x-axis",
            "x-axis is closest to the query vector");
        results[1].DocumentId.Should().Be("y-axis",
            "y-axis is second closest");
    }

    [Fact]
    public async Task QueryAsync_TopKLimitsResults()
    {
        for (int i = 0; i < 10; i++)
        {
            var v = new float[3];
            v[i % 3] = 1f;
            await _sut.UpsertAsync($"doc-{i}", v);
        }

        var results = await _sut.QueryAsync(new float[] { 1f, 0f, 0f }, topK: 3);

        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task QueryAsync_IdenticalVectors_ReturnsScoreOfOne()
    {
        var vector = new float[] { 0.5f, 0.5f, 0.5f };
        await _sut.UpsertAsync("doc-1", vector);

        var results = await _sut.QueryAsync(vector, topK: 1);

        results.Should().HaveCount(1);
        results[0].Score.Should().BeApproximately(1.0, 0.001,
            "identical vectors should have cosine similarity of 1.0");
    }

    [Fact]
    public async Task QueryAsync_OppositeVectors_ReturnsNegativeScore()
    {
        await _sut.UpsertAsync("positive", new float[] { 1f, 1f, 1f });

        var query = new float[] { -1f, -1f, -1f };
        var results = await _sut.QueryAsync(query, topK: 1);

        results.Should().HaveCount(1);
        results[0].Score.Should().BeApproximately(-1.0, 0.001,
            "opposite vectors should have cosine similarity of -1.0");
    }

    [Fact]
    public async Task QueryAsync_WithMetadataFilter_FiltersResults()
    {
        var vec = new float[] { 1f, 0f, 0f };
        await _sut.UpsertAsync("chunk-1", vec,
            new Dictionary<string, string> { ["type"] = "knowledge_chunk" });
        await _sut.UpsertAsync("note-1", vec,
            new Dictionary<string, string> { ["type"] = "note" });
        await _sut.UpsertAsync("no-meta", vec);

        var filter = new Dictionary<string, string> { ["type"] = "knowledge_chunk" };
        var results = await _sut.QueryAsync(vec, topK: 10, filter: filter);

        results.Should().HaveCount(1);
        results[0].DocumentId.Should().Be("chunk-1");
    }

    [Fact]
    public async Task QueryAsync_FilterExcludesNullMetadata()
    {
        await _sut.UpsertAsync("no-meta", new float[] { 1f, 0f });

        var filter = new Dictionary<string, string> { ["type"] = "any" };
        var results = await _sut.QueryAsync(new float[] { 1f, 0f }, topK: 10, filter: filter);

        results.Should().BeEmpty("documents without metadata should not match any filter");
    }

    [Fact]
    public async Task QueryAsync_FilterRequiresAllKeysToMatch()
    {
        var vec = new float[] { 1f, 0f };
        await _sut.UpsertAsync("partial", vec,
            new Dictionary<string, string> { ["type"] = "chunk" });

        var filter = new Dictionary<string, string>
        {
            ["type"] = "chunk",
            ["boardId"] = "b1"
        };

        var results = await _sut.QueryAsync(vec, topK: 10, filter: filter);
        results.Should().BeEmpty("partial metadata match should not pass the filter");
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_ExistingDocument_RemovesIt()
    {
        await _sut.UpsertAsync("doc-1", new float[] { 1f, 0f });
        (await _sut.CountAsync()).Should().Be(1);

        await _sut.DeleteAsync("doc-1");

        (await _sut.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentDocument_IsNoOp()
    {
        await _sut.UpsertAsync("doc-1", new float[] { 1f, 0f });

        await _sut.DeleteAsync("doc-nonexistent");

        (await _sut.CountAsync()).Should().Be(1, "deleting a non-existent ID should not affect existing data");
    }

    #endregion

    #region DeleteBatchAsync

    [Fact]
    public async Task DeleteBatchAsync_RemovesMultipleDocuments()
    {
        await _sut.UpsertAsync("doc-1", new float[] { 1f, 0f });
        await _sut.UpsertAsync("doc-2", new float[] { 0f, 1f });
        await _sut.UpsertAsync("doc-3", new float[] { 1f, 1f });

        await _sut.DeleteBatchAsync(new[] { "doc-1", "doc-3" });

        (await _sut.CountAsync()).Should().Be(1);
        var results = await _sut.QueryAsync(new float[] { 0f, 1f }, topK: 10);
        results.Should().HaveCount(1);
        results[0].DocumentId.Should().Be("doc-2");
    }

    [Fact]
    public async Task DeleteBatchAsync_NullInput_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.DeleteBatchAsync(null!));
    }

    #endregion

    #region CountAsync

    [Fact]
    public async Task CountAsync_EmptyIndex_ReturnsZero()
    {
        var count = await _sut.CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task CountAsync_AfterInsertsAndDeletes_ReturnsCorrectCount()
    {
        await _sut.UpsertAsync("doc-1", new float[] { 1f, 0f });
        await _sut.UpsertAsync("doc-2", new float[] { 0f, 1f });
        await _sut.UpsertAsync("doc-3", new float[] { 1f, 1f });

        (await _sut.CountAsync()).Should().Be(3);

        await _sut.DeleteAsync("doc-2");

        (await _sut.CountAsync()).Should().Be(2);
    }

    #endregion

    #region Cosine similarity behavior via public API

    [Fact]
    public async Task QueryAsync_ZeroVectorInIndex_ScoresZero()
    {
        // A zero vector stored in the index should yield score 0
        await _sut.UpsertAsync("zero", new float[] { 0f, 0f, 0f });
        await _sut.UpsertAsync("nonzero", new float[] { 1f, 0f, 0f });

        var results = await _sut.QueryAsync(new float[] { 1f, 0f, 0f }, topK: 2);

        var zeroResult = results.First(r => r.DocumentId == "zero");
        zeroResult.Score.Should().BeApproximately(0.0, 0.001,
            "cosine similarity with zero vector should be 0");

        var nonzeroResult = results.First(r => r.DocumentId == "nonzero");
        nonzeroResult.Score.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public async Task QueryAsync_ParallelVectors_ScoreOne()
    {
        // Parallel vectors (same direction, different magnitude) should score ~1.0
        await _sut.UpsertAsync("short", new float[] { 1f, 2f, 3f });

        var query = new float[] { 2f, 4f, 6f }; // parallel to stored
        var results = await _sut.QueryAsync(query, topK: 1);

        results[0].Score.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public async Task QueryAsync_OrthogonalVectors_ScoreZero()
    {
        await _sut.UpsertAsync("x", new float[] { 1f, 0f, 0f });

        var query = new float[] { 0f, 1f, 0f }; // orthogonal to stored
        var results = await _sut.QueryAsync(query, topK: 1);

        results[0].Score.Should().BeApproximately(0.0, 0.001);
    }

    #endregion

    #region Thread safety

    [Fact]
    public async Task ConcurrentUpserts_DoNotCorruptIndex()
    {
        const int concurrency = 50;
        var tasks = new List<Task>();

        for (int i = 0; i < concurrency; i++)
        {
            var docId = $"doc-{i}";
            var vec = new float[] { i, i + 1, i + 2 };
            tasks.Add(_sut.UpsertAsync(docId, vec));
        }

        await Task.WhenAll(tasks);

        var count = await _sut.CountAsync();
        count.Should().Be(concurrency);
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_DoNotThrow()
    {
        // Pre-populate
        for (int i = 0; i < 20; i++)
            await _sut.UpsertAsync($"doc-{i}", new float[] { i, i + 1 });

        var queryVec = new float[] { 10f, 11f };
        var tasks = new List<Task>();

        // Mix reads and writes concurrently
        for (int i = 0; i < 50; i++)
        {
            if (i % 2 == 0)
                tasks.Add(_sut.QueryAsync(queryVec, topK: 5));
            else
                tasks.Add(_sut.UpsertAsync($"new-{i}", new float[] { i, i }));
        }

        // Should not throw
        await Task.WhenAll(tasks);
    }

    #endregion
}
