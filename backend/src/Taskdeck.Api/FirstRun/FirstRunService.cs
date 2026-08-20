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
    private readonly Action<string> _browserLauncher;
    private readonly Func<string, CancellationToken, Task<bool>> _readinessProbe;
    private readonly bool _isPackagedDesktop;
    private readonly Func<bool> _browserSuppressed;

    public FirstRunService(
        ILogger<FirstRunService> logger,
        FirstRunSettings settings)
        : this(
            logger,
            settings,
            LaunchBrowser,
            WaitForReadinessAsync,
            DesktopRuntime.IsPackagedDesktop,
            DesktopRuntime.IsBrowserSuppressedEnvironment)
    {
    }

    internal FirstRunService(
        ILogger<FirstRunService> logger,
        FirstRunSettings settings,
        Action<string> browserLauncher,
        Func<string, CancellationToken, Task<bool>> readinessProbe,
        bool isPackagedDesktop,
        Func<bool> browserSuppressed)
    {
        _logger = logger;
        _settings = settings;
        _browserLauncher = browserLauncher;
        _readinessProbe = readinessProbe;
        _isPackagedDesktop = isPackagedDesktop;
        _browserSuppressed = browserSuppressed;
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
        if (!DesktopRuntime.ShouldOpenBrowser(
                _isPackagedDesktop,
                _settings.AutoOpenBrowser,
                _browserSuppressed()))
        {
            return;
        }

        OpenBrowser(url);
    }

    internal async Task ReportPackagedReadyAndOpenBrowserAsync(
        string url,
        CancellationToken cancellationToken)
    {
        bool isReady;
        try
        {
            isReady = await _readinessProbe(url, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            DesktopRuntime.WriteReadinessFailure();
            return;
        }

        if (!isReady)
        {
            DesktopRuntime.WriteReadinessFailure();
            return;
        }

        DesktopRuntime.WriteReady(url);
        TryOpenBrowser(url);
    }

    private void OpenBrowser(string url)
    {
        _logger.LogInformation("First-run: Opening browser at {Url}", url);

        try
        {
            _browserLauncher(url);
        }
        catch (Exception ex) when (!_isPackagedDesktop)
        {
            _logger.LogWarning(ex, "First-run: Failed to open browser at {Url}.", url);
        }
        catch
        {
            // Packaged output must remain stable and redacted. The printed URL is sufficient for recovery.
            _logger.LogWarning("First-run: The default browser could not be opened. Open {Url} manually.", url);
        }
    }

    private static void LaunchBrowser(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static async Task<bool> WaitForReadinessAsync(
        string baseUrl,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var readyUri = new Uri(new Uri(baseUrl.EndsWith('/') ? baseUrl : $"{baseUrl}/"), "health/ready");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);

        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await client.GetAsync(
                    readyUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                // The listener may be live before the readiness endpoint accepts requests.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A per-request timeout is retryable until the bounded startup deadline.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        return false;
    }
}
