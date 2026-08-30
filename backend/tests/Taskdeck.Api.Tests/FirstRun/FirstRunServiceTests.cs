using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Api.FirstRun;
using Xunit;

namespace Taskdeck.Api.Tests.FirstRun;

public class FirstRunServiceTests
{
    [Fact]
    public void TryOpenBrowser_WhenGenericAutoOpenIsDisabled_DoesNotLaunch()
    {
        var launches = new List<string>();
        var sut = CreateService(
            new FirstRunSettings { AutoOpenBrowser = false },
            launches.Add,
            isPackagedDesktop: false,
            browserSuppressed: false);

        sut.TryOpenBrowser("http://127.0.0.1:5000");

        Assert.Empty(launches);
    }

    [Fact]
    public void TryOpenBrowser_WhenGenericAutoOpenIsEnabled_LaunchesInjectedBrowser()
    {
        var launches = new List<string>();
        var sut = CreateService(
            new FirstRunSettings { AutoOpenBrowser = true },
            launches.Add,
            isPackagedDesktop: false,
            browserSuppressed: false);

        sut.TryOpenBrowser("http://127.0.0.1:5000");

        Assert.Equal(new[] { "http://127.0.0.1:5000" }, launches);
    }

    [Fact]
    public void TryOpenBrowser_WhenPackaged_UsesDesktopDefaultWithoutChangingGenericSetting()
    {
        var launches = new List<string>();
        var sut = CreateService(
            new FirstRunSettings { AutoOpenBrowser = false },
            launches.Add,
            isPackagedDesktop: true,
            browserSuppressed: false);

        sut.TryOpenBrowser("http://127.0.0.1:54321");

        Assert.Equal(new[] { "http://127.0.0.1:54321" }, launches);
    }

    [Fact]
    public void TryOpenBrowser_WhenCiContainerOrExplicitHeadlessSuppressesIt_DoesNotLaunch()
    {
        var launches = new List<string>();
        var sut = CreateService(
            new FirstRunSettings { AutoOpenBrowser = true },
            launches.Add,
            isPackagedDesktop: true,
            browserSuppressed: true);

        sut.TryOpenBrowser("http://127.0.0.1:5000");

        Assert.Empty(launches);
    }

    [Fact]
    public async Task ReportPackagedReadyAndOpenBrowserAsync_WaitsForReadinessBeforeLaunch()
    {
        var readiness = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var launches = new List<string>();
        var sut = new FirstRunService(
            NullLogger<FirstRunService>.Instance,
            new FirstRunSettings(),
            launches.Add,
            (_, _) => readiness.Task,
            isPackagedDesktop: true,
            browserSuppressed: () => false);

        var reportTask = sut.ReportPackagedReadyAndOpenBrowserAsync(
            "http://127.0.0.1:54321",
            CancellationToken.None);
        Assert.Empty(launches);

        readiness.SetResult(true);
        await reportTask;

        Assert.Equal(new[] { "http://127.0.0.1:54321" }, launches);
    }

    [Fact]
    public async Task ReportPackagedReadyAndOpenBrowserAsync_WhenReadinessFails_DoesNotLaunch()
    {
        var launches = new List<string>();
        var sut = new FirstRunService(
            NullLogger<FirstRunService>.Instance,
            new FirstRunSettings(),
            launches.Add,
            (_, _) => Task.FromResult(false),
            isPackagedDesktop: true,
            browserSuppressed: () => false);

        await sut.ReportPackagedReadyAndOpenBrowserAsync(
            "http://127.0.0.1:54321",
            CancellationToken.None);

        Assert.Empty(launches);
    }

    [Fact]
    public void TryOpenBrowser_WhenPackagedLauncherFails_RemainsNonFatal()
    {
        var sut = CreateService(
            new FirstRunSettings(),
            _ => throw new InvalidOperationException("synthetic raw provider-like detail"),
            isPackagedDesktop: true,
            browserSuppressed: false);

        var exception = Record.Exception(() => sut.TryOpenBrowser("http://127.0.0.1:5000"));

        Assert.Null(exception);
    }

    [Fact]
    public void FirstRunSettings_DefaultsAreConservativeForGenericHosts()
    {
        var settings = new FirstRunSettings();

        Assert.False(settings.AutoOpenBrowser);
        Assert.Equal(5000, settings.Port);
        Assert.True(settings.ResolveAppDataDbPath);
    }

    private static FirstRunService CreateService(
        FirstRunSettings settings,
        Action<string> browserLauncher,
        bool isPackagedDesktop,
        bool browserSuppressed)
        => new(
            NullLogger<FirstRunService>.Instance,
            settings,
            browserLauncher,
            (_, _) => Task.FromResult(true),
            isPackagedDesktop,
            () => browserSuppressed);
}
