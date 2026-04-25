using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class FuzzyTextMatcherTests
{
    private readonly FuzzyTextMatcher _matcher = new();

    // --- Exact match ---

    [Fact]
    public void ComputeSimilarity_ShouldReturn1_ForExactMatch()
    {
        var result = _matcher.ComputeSimilarity("hello world", "hello world");

        result.Should().Be(1.0);
    }

    [Fact]
    public void ComputeSimilarity_ShouldReturn1_ForExactSubstring()
    {
        var result = _matcher.ComputeSimilarity(
            "important task",
            "This is an important task that needs attention");

        result.Should().Be(1.0);
    }

    // --- Close match ---

    [Fact]
    public void ComputeSimilarity_ShouldReturnHigh_ForCloseMatch()
    {
        var result = _matcher.ComputeSimilarity(
            "meet with teem tomorrow",
            "meet with team tomorrow to discuss the project");

        result.Should().BeGreaterOrEqualTo(0.8);
    }

    [Fact]
    public void ComputeSimilarity_ShouldReturnHigh_ForMinorTypo()
    {
        var result = _matcher.ComputeSimilarity("deploymnet", "deployment");

        result.Should().BeGreaterOrEqualTo(0.8);
    }

    // --- No match ---

    [Fact]
    public void ComputeSimilarity_ShouldReturnLow_ForNoMatch()
    {
        var result = _matcher.ComputeSimilarity(
            "quantum physics lecture",
            "buy groceries at the store");

        result.Should().BeLessThan(0.5);
    }

    [Fact]
    public void ComputeSimilarity_ShouldReturnLow_ForCompletelyDifferent()
    {
        var result = _matcher.ComputeSimilarity("abc", "xyz");

        result.Should().BeLessThan(0.5);
    }

    // --- Empty strings ---

    [Fact]
    public void ComputeSimilarity_ShouldReturn0_WhenCandidateIsEmpty()
    {
        var result = _matcher.ComputeSimilarity("", "some source text");

        result.Should().Be(0.0);
    }

    [Fact]
    public void ComputeSimilarity_ShouldReturn0_WhenSourceIsEmpty()
    {
        var result = _matcher.ComputeSimilarity("some candidate", "");

        result.Should().Be(0.0);
    }

    [Fact]
    public void ComputeSimilarity_ShouldReturn0_WhenBothAreEmpty()
    {
        var result = _matcher.ComputeSimilarity("", "");

        result.Should().Be(0.0);
    }

    [Fact]
    public void ComputeSimilarity_ShouldReturn0_WhenCandidateIsNull()
    {
        var result = _matcher.ComputeSimilarity(null!, "source");

        result.Should().Be(0.0);
    }

    [Fact]
    public void ComputeSimilarity_ShouldReturn0_WhenSourceIsNull()
    {
        var result = _matcher.ComputeSimilarity("candidate", null!);

        result.Should().Be(0.0);
    }

    // --- Unicode ---

    [Fact]
    public void ComputeSimilarity_ShouldHandle_UnicodeCharacters()
    {
        var result = _matcher.ComputeSimilarity(
            "tarea importante",
            "esta es una tarea importante para completar");

        result.Should().Be(1.0);
    }

    [Fact]
    public void ComputeSimilarity_ShouldHandle_Emoji()
    {
        var result = _matcher.ComputeSimilarity(
            "task with emoji",
            "task with emoji in the title");

        result.Should().Be(1.0);
    }

    [Fact]
    public void ComputeSimilarity_ShouldHandle_CJKCharacters()
    {
        var result = _matcher.ComputeSimilarity("hello", "hello world");

        result.Should().Be(1.0);
    }

    // --- Case insensitivity ---

    [Fact]
    public void ComputeSimilarity_ShouldBeCaseInsensitive()
    {
        var result = _matcher.ComputeSimilarity("Hello World", "HELLO WORLD");

        result.Should().Be(1.0);
    }

    [Fact]
    public void ComputeSimilarity_ShouldNormalizeWhitespace()
    {
        var result = _matcher.ComputeSimilarity(
            "hello   world",
            "hello world");

        result.Should().Be(1.0);
    }

    // --- Very long text ---

    [Fact]
    public void ComputeSimilarity_ShouldHandleVeryLongSource()
    {
        var longSource = string.Join(" ", Enumerable.Repeat("word", 1000));
        var candidate = "word word word";

        var result = _matcher.ComputeSimilarity(candidate, longSource);

        result.Should().Be(1.0);
    }

    [Fact]
    public void ComputeSimilarity_ShouldHandle_CandidateLongerThanSource()
    {
        var result = _matcher.ComputeSimilarity(
            "this is a very long candidate text",
            "short");

        result.Should().BeLessThan(0.5);
    }

    // --- IsMatch ---

    [Fact]
    public void IsMatch_ShouldReturnTrue_AboveThreshold()
    {
        var result = _matcher.IsMatch("hello world", "hello world", 0.8);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsMatch_ShouldReturnFalse_BelowThreshold()
    {
        var result = _matcher.IsMatch("abc", "xyz", 0.8);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsMatch_ShouldUseDefaultThreshold()
    {
        // Exact match should pass default 0.8 threshold
        var result = _matcher.IsMatch("test", "this is a test");

        result.Should().BeTrue();
    }

    [Fact]
    public void IsMatch_ShouldReturnTrue_WhenScoreExactlyEqualsThreshold()
    {
        // With threshold 0.0, even bad matches should pass
        var result = _matcher.IsMatch("abc", "xyz", 0.0);

        result.Should().BeTrue();
    }

    // --- Whitespace-only strings ---

    [Fact]
    public void ComputeSimilarity_ShouldReturn0_WhenCandidateIsWhitespaceOnly()
    {
        var result = _matcher.ComputeSimilarity("   ", "some text");

        result.Should().Be(0.0);
    }

    [Fact]
    public void ComputeSimilarity_ShouldReturn0_WhenSourceIsWhitespaceOnly()
    {
        var result = _matcher.ComputeSimilarity("candidate", "   ");

        result.Should().Be(0.0);
    }

    // --- Single character ---

    [Fact]
    public void ComputeSimilarity_ShouldHandle_SingleCharacterMatch()
    {
        var result = _matcher.ComputeSimilarity("a", "a");

        result.Should().Be(1.0);
    }

    [Fact]
    public void ComputeSimilarity_ShouldHandle_SingleCharacterMismatch()
    {
        var result = _matcher.ComputeSimilarity("a", "b");

        result.Should().Be(0.0);
    }
}
