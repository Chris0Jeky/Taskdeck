using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Taskdeck.Api.FirstRun;

namespace Taskdeck.Api.Tests.FirstRun;

public class FirstRunServiceTests
{
    // ---- TryOpenBrowser — guarded by AutoOpenBrowser flag --------------------

    [Fact]
    public void TryOpenBrowser_WhenAutoOpenBrowserFalse_DoesNotThrow()
    {
        var settings = new FirstRunSettings { AutoOpenBrowser = false };
        var sut = new FirstRunService(NullLogger<FirstRunService>.Instance, settings);

        // Should be a no-op, no exception.
        sut.TryOpenBrowser("http://localhost:5000");
    }

    [Fact]
    public void TryOpenBrowser_WhenAutoOpenBrowserTrue_AndCiEnvSet_DoesNotThrow()
    {
        // Simulate CI environment so the browser is never actually opened.
        var prevValue = Environment.GetEnvironmentVariable("TASKDECK_HEADLESS");
        Environment.SetEnvironmentVariable("TASKDECK_HEADLESS", "1");
        try
        {
            var settings = new FirstRunSettings { AutoOpenBrowser = true, Port = 5000 };
            var sut = new FirstRunService(NullLogger<FirstRunService>.Instance, settings);

            sut.TryOpenBrowser("http://localhost:5000"); // should be a no-op in headless mode
        }
        finally
        {
            Environment.SetEnvironmentVariable("TASKDECK_HEADLESS", prevValue);
        }
    }

    // ---- FirstRunSettings defaults ------------------------------------------

    [Fact]
    public void FirstRunSettings_DefaultsAreConservative()
    {
        var settings = new FirstRunSettings();

        Assert.False(settings.AutoOpenBrowser, "AutoOpenBrowser must default to false");
        Assert.Equal(5000, settings.Port);
        Assert.True(settings.ResolveAppDataDbPath);
    }
}
