using FluentAssertions;
using Taskdeck.Infrastructure.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class InMemoryEmbeddingGeneratorTests
{
    private readonly InMemoryEmbeddingGenerator _sut = new(dimensions: 64);

    #region Constructor

    [Fact]
    public void Constructor_DefaultDimensions_Is384()
    {
        var gen = new InMemoryEmbeddingGenerator();
        gen.Dimensions.Should().Be(384);
    }

    [Fact]
    public void Constructor_CustomDimensions_Honored()
    {
        var gen = new InMemoryEmbeddingGenerator(dimensions: 128);
        gen.Dimensions.Should().Be(128);
    }

    [Fact]
    public void Constructor_ZeroDimensions_ThrowsArgumentOutOfRange()
    {
        var act = () => new InMemoryEmbeddingGenerator(dimensions: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_NegativeDimensions_ThrowsArgumentOutOfRange()
    {
        var act = () => new InMemoryEmbeddingGenerator(dimensions: -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region IsAvailable

    [Fact]
    public void IsAvailable_AlwaysReturnsTrue()
    {
        _sut.IsAvailable.Should().BeTrue();
    }

    #endregion

    #region GenerateAsync

    [Fact]
    public async Task GenerateAsync_ProducesCorrectDimensionality()
    {
        var embedding = await _sut.GenerateAsync("hello world");

        embedding.Length.Should().Be(64);
    }

    [Fact]
    public async Task GenerateAsync_DeterministicForSameInput()
    {
        var embedding1 = await _sut.GenerateAsync("test input");
        var embedding2 = await _sut.GenerateAsync("test input");

        embedding1.ToArray().Should().BeEquivalentTo(embedding2.ToArray(),
            "same text should always produce the same vector");
    }

    [Fact]
    public async Task GenerateAsync_DifferentInputs_ProduceDifferentVectors()
    {
        var embedding1 = await _sut.GenerateAsync("hello");
        var embedding2 = await _sut.GenerateAsync("goodbye");

        embedding1.ToArray().Should().NotBeEquivalentTo(embedding2.ToArray(),
            "different texts should produce different vectors");
    }

    [Fact]
    public async Task GenerateAsync_OutputIsNormalized()
    {
        var embedding = await _sut.GenerateAsync("test normalization");
        var values = embedding.ToArray();

        // Compute L2 norm
        float norm = 0f;
        foreach (var v in values)
            norm += v * v;
        norm = MathF.Sqrt(norm);

        norm.Should().BeApproximately(1.0f, 0.001f,
            "generated embeddings should be L2-normalized");
    }

    [Fact]
    public async Task GenerateAsync_EmptyString_ReturnsZeroVector()
    {
        var embedding = await _sut.GenerateAsync("");
        var values = embedding.ToArray();

        values.Should().AllSatisfy(v => v.Should().Be(0f),
            "empty string should produce a zero vector");
    }

    [Fact]
    public async Task GenerateAsync_NullInput_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.GenerateAsync(null!));
    }

    #endregion

    #region GenerateBatchAsync

    [Fact]
    public async Task GenerateBatchAsync_PositionalAlignment()
    {
        var texts = new List<string> { "alpha", "beta", "gamma" };

        var batch = await _sut.GenerateBatchAsync(texts);

        batch.Should().HaveCount(3);

        // Each batch result should match individual generation
        for (int i = 0; i < texts.Count; i++)
        {
            var individual = await _sut.GenerateAsync(texts[i]);
            batch[i].ToArray().Should().BeEquivalentTo(individual.ToArray(),
                $"batch[{i}] should match individual generation for '{texts[i]}'");
        }
    }

    [Fact]
    public async Task GenerateBatchAsync_NullInput_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.GenerateBatchAsync(null!));
    }

    [Fact]
    public async Task GenerateBatchAsync_CancellationRespected()
    {
        var texts = Enumerable.Range(0, 1000).Select(i => $"text-{i}").ToList();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.GenerateBatchAsync(texts, cts.Token));
    }

    [Fact]
    public async Task GenerateBatchAsync_EmptyList_ReturnsEmptyList()
    {
        var batch = await _sut.GenerateBatchAsync(new List<string>());

        batch.Should().BeEmpty();
    }

    #endregion

    #region Cross-instance determinism

    [Fact]
    public async Task DifferentInstances_SameDimensions_ProduceSameVectors()
    {
        var gen1 = new InMemoryEmbeddingGenerator(dimensions: 64);
        var gen2 = new InMemoryEmbeddingGenerator(dimensions: 64);

        var emb1 = await gen1.GenerateAsync("cross-instance test");
        var emb2 = await gen2.GenerateAsync("cross-instance test");

        emb1.ToArray().Should().BeEquivalentTo(emb2.ToArray(),
            "determinism must hold across instances");
    }

    #endregion
}
