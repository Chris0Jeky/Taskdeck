namespace Taskdeck.Api.FirstRun;

/// <summary>
/// Configuration for first-run automatic setup behaviour.
/// </summary>
public sealed class FirstRunSettings
{
    /// <summary>
    /// When true the app opens the browser to the local URL after startup.
    /// Defaults to false so it never fires in CI or server deployments.
    /// </summary>
    public bool AutoOpenBrowser { get; set; } = false;

    /// <summary>
    /// Port the API listens on. Used to build the browser URL.
    /// </summary>
    public int Port { get; set; } = 5000;

    /// <summary>
    /// When true the first-run service resolves the DB path to the OS
    /// AppData/local-share folder if no explicit path is configured.
    /// Defaults to true.
    /// </summary>
    public bool ResolveAppDataDbPath { get; set; } = true;
}
