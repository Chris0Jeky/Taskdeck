using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace Taskdeck.Api.FirstRun;

/// <summary>
/// Runtime policy for the explicitly marked Windows desktop package. Source/server builds keep
/// their existing hosting defaults; the release workflow opts into this posture at publish time.
/// </summary>
internal static class DesktopRuntime
{
    internal const int DefaultPort = 5000;
    internal const string PackageMetadataKey = "TaskdeckDesktopPackage";

    private static int _fatalHandlerInstalled;

    internal static bool IsPackagedDesktop => typeof(DesktopRuntime).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(attribute =>
            string.Equals(attribute.Key, PackageMetadataKey, StringComparison.Ordinal)
            && string.Equals(attribute.Value, "true", StringComparison.OrdinalIgnoreCase));

    internal static WebApplicationBuilder CreateWebApplicationBuilder(string[] args)
    {
        if (!IsPackagedDesktop)
        {
            return WebApplication.CreateBuilder(args);
        }

        return WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });
    }

    internal static string ResolveContentRoot(
        bool isPackagedDesktop,
        string executableDirectory,
        string currentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);
        return Path.GetFullPath(isPackagedDesktop ? executableDirectory : currentDirectory);
    }

    internal static bool IsBootstrapHeadlessEnvironment(bool isPackagedDesktop)
        => IsExplicitHeadlessEnvironment()
            || (!isPackagedDesktop && IsCiEnvironment());

    internal static bool IsBrowserSuppressedEnvironment()
        => IsExplicitHeadlessEnvironment() || IsCiEnvironment();

    internal static bool ResolveBootstrapHeadless(
        bool isPackagedDesktop,
        bool isCi,
        bool isContainer,
        bool isExplicitHeadless)
        => isContainer || isExplicitHeadless || (!isPackagedDesktop && isCi);

    internal static bool ResolveBrowserSuppressed(
        bool isCi,
        bool isContainer,
        bool isExplicitHeadless)
        => isCi || isContainer || isExplicitHeadless;

    internal static string? ResolvePackagedDefaultListenUrl(
        bool isPackagedDesktop,
        bool hasExplicitListenConfiguration,
        Func<int, bool> canBindLoopbackPort)
    {
        ArgumentNullException.ThrowIfNull(canBindLoopbackPort);
        if (!isPackagedDesktop || hasExplicitListenConfiguration)
        {
            return null;
        }

        return canBindLoopbackPort(DefaultPort)
            ? $"http://127.0.0.1:{DefaultPort}"
            : "http://127.0.0.1:0";
    }

    internal static bool HasExplicitListenConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return HasValue(configuration["urls"])
            || HasValue(configuration["ASPNETCORE_URLS"])
            || HasValue(configuration["http_ports"])
            || HasValue(configuration["https_ports"])
            || HasValue(configuration["ASPNETCORE_HTTP_PORTS"])
            || HasValue(configuration["ASPNETCORE_HTTPS_PORTS"])
            || configuration.GetSection("Kestrel:Endpoints").GetChildren().Any();
    }

    internal static bool CanBindLoopbackPort(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
            return true;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return false;
        }
        finally
        {
            listener.Stop();
        }
    }

    internal static string ResolveUserFacingUrl(IEnumerable<string>? addresses)
    {
        var parsed = (addresses ?? Array.Empty<string>())
            .Select(address => Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri : null)
            .Where(uri => uri is not null && uri.Scheme is "http" or "https")
            .Cast<Uri>()
            .ToList();

        var selected = parsed.FirstOrDefault(uri => uri.Host == "127.0.0.1")
            ?? parsed.FirstOrDefault(uri => IsLoopbackHost(uri.Host))
            ?? parsed.FirstOrDefault(uri => IsWildcardHost(uri.Host));

        if (selected is null)
        {
            throw new InvalidOperationException(
                "The packaged desktop listener did not publish an actual loopback address.");
        }

        var host = IsWildcardHost(selected.Host)
            ? "127.0.0.1"
            : selected.Host;
        var authorityHost = host.Contains(':', StringComparison.Ordinal) ? $"[{host}]" : host;
        return $"{selected.Scheme}://{authorityHost}:{selected.Port}";
    }

    internal static bool ShouldOpenBrowser(
        bool isPackagedDesktop,
        bool configuredAutoOpen,
        bool browserSuppressed)
        => !browserSuppressed && (isPackagedDesktop || configuredAutoOpen);

    internal static void ConfigurePackagedListenUrl(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var url = ResolvePackagedDefaultListenUrl(
            IsPackagedDesktop,
            HasExplicitListenConfiguration(builder.Configuration),
            CanBindLoopbackPort);
        if (url is not null)
        {
            builder.WebHost.UseUrls(url);
        }
    }

    internal static void InstallPackagedFatalHandler()
    {
        if (!IsPackagedDesktop || Interlocked.Exchange(ref _fatalHandlerInstalled, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, _) =>
        {
            WriteFatalStartup();
            WaitForFailureAcknowledgement();
        };
    }

    internal static void WriteStarting()
    {
        Console.WriteLine("TASKDECK_DESKTOP_STARTING");
        Console.WriteLine("Taskdeck is starting. Keep this window open while you use Taskdeck.");
        Console.WriteLine("Press Ctrl+C to stop Taskdeck safely.");
    }

    internal static void WriteDataLocation(string dataDirectory)
    {
        Console.WriteLine("TASKDECK_DESKTOP_DATA");
        Console.WriteLine($"Your Taskdeck data is stored in: {Path.GetFullPath(dataDirectory)}");
    }

    internal static void WriteReady(string url)
    {
        Console.WriteLine($"TASKDECK_DESKTOP_READY url={url}");
        Console.WriteLine($"Taskdeck is ready at {url}");
    }

    internal static void WriteReadinessFailure()
    {
        Console.Error.WriteLine("TASKDECK_DESKTOP_FATAL code=readiness_timeout");
        Console.Error.WriteLine(
            "Taskdeck started but did not become ready. Keep this window open and retry after checking the data folder is writable.");
    }

    internal static void WriteStopping()
    {
        Console.WriteLine("TASKDECK_DESKTOP_SHUTTING_DOWN");
        Console.WriteLine("Taskdeck is stopping safely.");
    }

    internal static void WriteStopped()
    {
        Console.WriteLine("TASKDECK_DESKTOP_STOPPED");
        Console.WriteLine("Taskdeck stopped. You can close this window.");
    }

    internal static void WriteFatalStartup()
    {
        Console.Error.WriteLine("TASKDECK_DESKTOP_FATAL code=startup_failed");
        Console.Error.WriteLine(
            "Taskdeck could not start. Check that the configured port is available and the data folder is writable. No settings were printed.");
    }

    internal static void WaitForFailureAcknowledgement()
    {
        if (IsBrowserSuppressedEnvironment() || !Environment.UserInteractive || Console.IsInputRedirected)
        {
            return;
        }

        Console.Error.WriteLine("Press Enter to close this window.");
        _ = Console.ReadLine();
    }

    private static bool IsCiEnvironment()
        => HasValue(Environment.GetEnvironmentVariable("CI"))
            || HasValue(Environment.GetEnvironmentVariable("TF_BUILD"))
            || HasValue(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

    // Taskdeck's container images set TASKDECK_HEADLESS explicitly. Do not make ambient .NET
    // container flags change the established generic server or standalone MCP persistence posture.
    private static bool IsExplicitHeadlessEnvironment()
        => HasValue(Environment.GetEnvironmentVariable("TASKDECK_HEADLESS"));

    private static bool IsLoopbackHost(string host)
        => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host.Trim('[', ']'), out var address)
                && IPAddress.IsLoopback(address));

    private static bool IsWildcardHost(string host)
        => host is "0.0.0.0" or "::" or "[::]";

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);
}
