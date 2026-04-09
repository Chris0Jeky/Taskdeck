using System.Diagnostics;

namespace Taskdeck.Integration.Tests.Fixtures;

/// <summary>
/// Checks whether Docker is available on the host machine.
/// Used to skip Testcontainers-based tests when Docker is not running,
/// allowing <c>dotnet test backend/Taskdeck.sln</c> to succeed even
/// on machines without Docker.
/// </summary>
public static class DockerAvailableCheck
{
    private static readonly Lazy<bool> IsAvailableLazy = new(CheckDocker);

    /// <summary>
    /// Returns true if Docker is available and responsive on the host.
    /// The result is cached for the lifetime of the process.
    /// </summary>
    public static bool IsAvailable => IsAvailableLazy.Value;

    /// <summary>
    /// Skip message for use with xUnit's Skip property.
    /// </summary>
    public const string SkipReason = "Docker is not available on this machine";

    private static bool CheckDocker()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "info",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit(TimeSpan.FromSeconds(10));
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
