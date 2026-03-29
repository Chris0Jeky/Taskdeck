using FluentAssertions;
using Taskdeck.Infrastructure.Services;
using Xunit;

namespace Taskdeck.Api.Tests;

public class KnowledgeFtsSearchServiceSanitizeTests
{
    [Fact]
    public void SanitizeFtsQuery_NormalText_PassesThrough()
    {
        var result = KnowledgeFtsSearchService.SanitizeFtsQuery("hello world");

        result.Should().Be("hello world");
    }

    [Fact]
    public void SanitizeFtsQuery_FtsSpecialCharacters_StripsAll()
    {
        var result = KnowledgeFtsSearchService.SanitizeFtsQuery("test\"query'with(special)chars*and-more+colons:carets^braces{}");

        result.Should().Be("test query with special chars and more colons carets braces");
    }

    [Fact]
    public void SanitizeFtsQuery_AllSpecialChars_ReturnsEmpty()
    {
        var result = KnowledgeFtsSearchService.SanitizeFtsQuery("\"'()*-+:^{}");

        result.Should().BeEmpty();
    }

    [Fact]
    public void SanitizeFtsQuery_MixedContent_PreservesWordsAndRemovesSpecials()
    {
        var result = KnowledgeFtsSearchService.SanitizeFtsQuery("project:\"alpha\" OR (beta AND -gamma)");

        result.Should().Be("project alpha OR beta AND gamma");
    }

    [Fact]
    public void SanitizeFtsQuery_ExtraWhitespace_NormalizesToSingleSpaces()
    {
        var result = KnowledgeFtsSearchService.SanitizeFtsQuery("  hello   world  ");

        result.Should().Be("hello world");
    }

    [Fact]
    public void SanitizeFtsQuery_TabsAndNewlines_TreatedAsWhitespace()
    {
        var result = KnowledgeFtsSearchService.SanitizeFtsQuery("hello\tworld\nfoo\rbar");

        result.Should().Be("hello world foo bar");
    }

    [Fact]
    public void SanitizeFtsQuery_SingleWord_PassesThrough()
    {
        var result = KnowledgeFtsSearchService.SanitizeFtsQuery("search");

        result.Should().Be("search");
    }
}
