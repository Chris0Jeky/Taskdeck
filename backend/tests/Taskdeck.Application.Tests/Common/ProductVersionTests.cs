using System.Reflection;
using FluentAssertions;
using Taskdeck.Application.Common;
using Xunit;

namespace Taskdeck.Application.Tests.Common;

public class ProductVersionTests
{
    [Fact]
    public void Value_IsNeverBlank()
    {
        ProductVersion.Value.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Value_CarriesNoBuildMetadataSuffix()
    {
        // A "+<sha>" suffix would stop the reported version matching the release tag.
        ProductVersion.Value.Should().NotContain("+");
    }

    [Fact]
    public void Value_MatchesTheStampedAssemblyInformationalVersion()
    {
        var stamped = typeof(ProductVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        ProductVersion.Value.Should().Be(ProductVersion.Normalize(stamped));
    }

    [Fact]
    public void Value_IsTheDevelopmentFallback_ForAnUnstampedLocalBuild()
    {
        // Local and CI builds inject no /p:Version, so backend/Directory.Build.props
        // supplies 0.0.0-dev. If this ever fails, a version WAS injected into a test
        // build — check the invoking command before relaxing the assertion.
        ProductVersion.Value.Should().Be(ProductVersion.DevelopmentFallback);
    }

    [Fact]
    public void Resolve_ReadsTheInformationalVersionOfTheGivenAssembly()
    {
        ProductVersion.Resolve(typeof(ProductVersion).Assembly)
            .Should()
            .Be(ProductVersion.Value);
    }

    [Fact]
    public void Resolve_ThrowsOnNullAssembly()
    {
        var act = () => ProductVersion.Resolve(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+abc1234")]
    public void Normalize_FallsBackWhenNothingUsableIsStamped(string? informationalVersion)
    {
        ProductVersion.Normalize(informationalVersion)
            .Should()
            .Be(ProductVersion.DevelopmentFallback);
    }

    [Theory]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("0.1.0+abc1234", "0.1.0")]
    [InlineData("1.2.3-rc.1+deadbee", "1.2.3-rc.1")]
    [InlineData("  0.2.0  ", "0.2.0")]
    [InlineData("0.0.0-dev", "0.0.0-dev")]
    public void Normalize_StripsBuildMetadataAndSurroundingWhitespace(string informationalVersion, string expected)
    {
        ProductVersion.Normalize(informationalVersion).Should().Be(expected);
    }
}
