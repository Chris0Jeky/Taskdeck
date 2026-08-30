using Microsoft.Extensions.Configuration;
using Taskdeck.Api.FirstRun;
using Taskdeck.Application.Services;
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
    [InlineData(true, true, "TASKDECK_DESKTOP_BOOTSTRAP jwt_created=true connector_created=true")]
    [InlineData(true, false, "TASKDECK_DESKTOP_BOOTSTRAP jwt_created=true connector_created=false")]
    [InlineData(false, true, "TASKDECK_DESKTOP_BOOTSTRAP jwt_created=false connector_created=true")]
    [InlineData(false, false, "TASKDECK_DESKTOP_BOOTSTRAP jwt_created=false connector_created=false")]
    public void FormatBootstrapIdentityMarker_UsesOnlyBoundedPerSecretLifecycles(
        bool jwtCreated,
        bool connectorCreated,
        string expected)
    {
        Assert.Equal(
            expected,
            DesktopRuntime.FormatBootstrapIdentityMarker(
                new BootstrapIdentityLifecycle(jwtCreated, connectorCreated)));
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

    [Fact]
    public void FormatFatalStartup_MapsTypedRetiredProviderFailureToStaticActionableOutput()
    {
        var exception = Assert.Throws<RetiredLlmProviderConfigurationException>(
            () => LlmProviderSelectionPolicy.ThrowIfRetiredProvider("Gemini"));

        var output = DesktopRuntime.FormatFatalStartup(exception);

        Assert.Equal(
            [
                "TASKDECK_DESKTOP_FATAL code=retired_provider_configuration",
                "Taskdeck could not start because retired Gemini provider configuration is still active. " +
                "Choose OpenAI, OpenAICompatible, Ollama, or Mock, then remove the retired Gemini selector, " +
                "child settings, and Docker Compose variable. Restart Taskdeck after updating them. " +
                "No settings were printed."
            ],
            output);
        Assert.DoesNotContain(exception.Message, output);
    }

    [Fact]
    public void FormatFatalStartup_MapsUnrelatedExceptionToGenericOutputWithoutContentLeak()
    {
        const string secretLikeContent = "synthetic-secret-never-print\r\nsynthetic-stack-line";

        var output = DesktopRuntime.FormatFatalStartup(
            new InvalidOperationException(secretLikeContent));

        Assert.Equal(
            [
                "TASKDECK_DESKTOP_FATAL code=startup_failed",
                "Taskdeck could not start. Check that the configured port is available and the data folder is writable. " +
                "No settings were printed."
            ],
            output);
        Assert.All(output, line => Assert.DoesNotContain(secretLikeContent, line));
        Assert.All(output, line => Assert.DoesNotContain("synthetic-secret", line));
        Assert.All(output, line => Assert.DoesNotContain("synthetic-stack", line));
    }

    [Fact]
    public void FormatRetiredProviderConfigurationIgnored_IsBoundedValueBlindWarningOutput()
    {
        var output = DesktopRuntime.FormatRetiredProviderConfigurationIgnored();

        Assert.Equal(
            [
                "TASKDECK_DESKTOP_WARNING code=retired_provider_configuration_ignored",
                "Taskdeck ignored retired Gemini provider settings left in this profile's environment. " +
                "No retired value was kept, logged, or printed. Remove the leftover Llm__Gemini__* " +
                "variables (and any Llm__Provider=Gemini) to clear this warning. The provider actually " +
                "in use is shown in Taskdeck's provider status."
            ],
            output);
        Assert.All(output, line => Assert.DoesNotContain("TASKDECK_DESKTOP_FATAL", line));
        // The notice fires for any dropped retired key, including one beside a valid live selector,
        // so it must never claim which provider ended up selected (#2233 review H-1).
        Assert.All(output, line => Assert.DoesNotContain("offline", line, StringComparison.OrdinalIgnoreCase));
        Assert.All(output, line => Assert.DoesNotContain("started with", line, StringComparison.OrdinalIgnoreCase));
        // The filter reads the Llm:Provider value to recognise the retired name, so the notice must
        // not promise that nothing was read; the enforceable promise is about retention (#2233 R2).
        Assert.All(output, line => Assert.DoesNotContain("were read", line, StringComparison.OrdinalIgnoreCase));
    }

    private static IConfiguration Configuration(string key, string value)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();
}
