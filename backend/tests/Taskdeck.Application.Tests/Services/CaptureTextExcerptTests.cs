using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class CaptureTextExcerptTests
{
    [Fact]
    public void Build_NormalizesWhitespaceBeforeApplyingTheLimit()
    {
        var excerpt = CaptureTextExcerpt.Build("  first\n\tsecond   third  ");

        excerpt.Should().Be("first second third");
    }

    [Fact]
    public void Build_DropsTheWholeSurrogatePair_WhenTheLimitFallsBetweenCodeUnits()
    {
        var text = new string('x', 199) + "😀tail";

        var excerpt = CaptureTextExcerpt.Build(text);

        excerpt.Should().Be(new string('x', 199));
        excerpt.Should().NotContain("😀");
        excerpt.Length.Should().Be(199);
    }

    [Fact]
    public void Build_PreservesTheSurrogatePair_WhenBothCodeUnitsFit()
    {
        var text = new string('x', 198) + "😀tail";

        var excerpt = CaptureTextExcerpt.Build(text);

        excerpt.Should().Be(new string('x', 198) + "😀");
        excerpt.Length.Should().Be(200);
    }
}
