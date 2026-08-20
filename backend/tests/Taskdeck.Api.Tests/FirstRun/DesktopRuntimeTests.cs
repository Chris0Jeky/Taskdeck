using Microsoft.Extensions.Configuration;
using Taskdeck.Api.FirstRun;
using Xunit;

namespace Taskdeck.Api.Tests.FirstRun;

public class DesktopRuntimeTests
{
    [Fact]
    public void DefaultTestBuild_DoesNotCarryDesktopPackageMetadata()
    {
        Assert.False(DesktopRuntime.IsPackagedDesktop);
    }

    [Theory]
    [InlineData(true, true, false, false, false)]
    [InlineData(false, true, false, false, true)]
    [InlineData(true, true, true, false, true)]
    [InlineData(true, true, false, true, true)]
    [InlineData(false, false, false, false, false)]
    public void ResolveBootstrapHeadless_SplitsPackagedCiFromFailClosedPostures(
        bool isPackaged,
        bool isCi,
        bool isContainer,
        bool isExplicitHeadless,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopRuntime.ResolveBootstrapHeadless(
                isPackaged,
                isCi,
                isContainer,
                isExplicitHeadless));
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, false, false, false)]
    public void ResolveBrowserSuppressed_CoversCiContainerAndExplicitHeadless(
        bool isCi,
        bool isContainer,
        bool isExplicitHeadless,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopRuntime.ResolveBrowserSuppressed(isCi, isContainer, isExplicitHeadless));
    }

    [Fact]
    public void ResolveContentRoot_UsesExecutableOnlyForMarkedPackage()
    {
        var executable = Path.GetFullPath(Path.Combine("root", "package"));
        var current = Path.GetFullPath(Path.Combine("root", "unrelated-cwd"));

        Assert.Equal(executable, DesktopRuntime.ResolveContentRoot(true, executable, current));
        Assert.Equal(current, DesktopRuntime.ResolveContentRoot(false, executable, current));
    }

    [Theory]
    [InlineData(true, false, true, "http://127.0.0.1:5000")]
    [InlineData(true, false, false, "http://127.0.0.1:0")]
    [InlineData(true, true, true, null)]
    [InlineData(false, false, true, null)]
    public void ResolvePackagedDefaultListenUrl_Uses5000ThenDynamicLoopbackOnlyForImplicitPackage(
        bool isPackaged,
        bool hasExplicitListenConfiguration,
        bool canBindDefault,
        string? expected)
    {
        Assert.Equal(
            expected,
            DesktopRuntime.ResolvePackagedDefaultListenUrl(
                isPackaged,
                hasExplicitListenConfiguration,
                _ => canBindDefault));
    }

    [Fact]
    public void HasExplicitListenConfiguration_RecognizesUrlsPortsAndKestrelEndpoints()
    {
        Assert.True(DesktopRuntime.HasExplicitListenConfiguration(Configuration("urls", "http://127.0.0.1:6000")));
        Assert.True(DesktopRuntime.HasExplicitListenConfiguration(Configuration("http_ports", "6001")));
        Assert.True(DesktopRuntime.HasExplicitListenConfiguration(
            Configuration("Kestrel:Endpoints:Http:Url", "http://127.0.0.1:6002")));
        Assert.False(DesktopRuntime.HasExplicitListenConfiguration(Configuration("FirstRun:Port", "6003")));
    }

    [Fact]
    public void ResolveUserFacingUrl_PrefersActualIpv4LoopbackAndNormalizesWildcard()
    {
        var preferred = DesktopRuntime.ResolveUserFacingUrl(
            new[] { "http://0.0.0.0:6000", "http://127.0.0.1:54321" });
        var normalizedWildcard = DesktopRuntime.ResolveUserFacingUrl(
            new[] { "http://0.0.0.0:6000" });

        Assert.Equal("http://127.0.0.1:54321", preferred);
        Assert.Equal("http://127.0.0.1:6000", normalizedWildcard);
    }

    [Fact]
    public void ResolveUserFacingUrl_RejectsMissingOrRemoteOnlyAddresses()
    {
        Assert.Throws<InvalidOperationException>(() => DesktopRuntime.ResolveUserFacingUrl(null));
        Assert.Throws<InvalidOperationException>(() => DesktopRuntime.ResolveUserFacingUrl(
            new[] { "http://192.0.2.10:5000" }));
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, false, false)]
    public void ShouldOpenBrowser_PreservesGenericOptInAndPackageDefault(
        bool isPackaged,
        bool configuredAutoOpen,
        bool browserSuppressed,
        bool expected)
    {
        Assert.Equal(
            expected,
            DesktopRuntime.ShouldOpenBrowser(isPackaged, configuredAutoOpen, browserSuppressed));
    }

    private static IConfiguration Configuration(string key, string value)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();
}
