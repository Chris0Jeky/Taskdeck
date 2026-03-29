using System.Diagnostics;

namespace Taskdeck.Api.FirstRun;

/// <summary>
/// Runtime first-run service. Handles post-startup concerns:
/// browser auto-open after the host is ready.
/// Configuration bootstrapping (JWT secret, DB path) is handled at build-time
/// by <see cref="FirstRunBootstrapper"/>.
/// </summary>
public sealed class FirstRunService
{
    private readonly ILogger<FirstRunService> _logger;
    private readonly FirstRunSettings _settings;

    public FirstRunService(
        ILogger<FirstRunService> logger,
        FirstRunSettings settings)
    {
        _logger = logger;
        _settings = settings;
    }

    /// <summary>
    /// Opens the browser to the given URL when
    /// <see cref="FirstRunSettings.AutoOpenBrowser"/> is enabled and the
    /// process is not running in a headless/CI context.
    /// </summary>
    /// <param name="url">The URL to open. Typically the actual listening address
    /// obtained from <c>IServerAddressesFeature</c>.</param>
    public void TryOpenBrowser(string url)
    {
        if (!_settings.AutoOpenBrowser)
        {
            return;
        }

        if (FirstRunBootstrapper.IsHeadlessEnvironment())
        {
            _logger.LogDebug(
                "First-run: AutoOpenBrowser skipped (headless/CI environment detected).");
            return;
        }

        _logger.LogInformation("First-run: Opening browser at {Url}", url);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            // Non-fatal: the user can always open the browser manually.
            _logger.LogWarning(ex, "First-run: Failed to open browser at {Url}.", url);
        }
    }
}
