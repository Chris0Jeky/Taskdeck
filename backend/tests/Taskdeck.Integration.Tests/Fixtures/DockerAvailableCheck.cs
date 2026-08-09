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
    public const string RequireDockerEnvironmentVariable = "TASKDECK_REQUIRE_DOCKER";
    public const string ForceDockerUnavailableEnvironmentVariable = "TASKDECK_FORCE_DOCKER_UNAVAILABLE";
    public const string RequiredDockerFailureMessage =
        "Docker is required for this test run but is unavailable. " +
        "Clear TASKDECK_REQUIRE_DOCKER for ordinary Dockerless local runs.";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReapTimeout = TimeSpan.FromSeconds(2);
    private static readonly Lazy<bool> IsAvailableLazy = new(CheckDocker);

    /// <summary>
    /// Returns true if Docker is available and responsive on the host.
    /// The result is cached for the lifetime of the process.
    /// </summary>
    public static bool IsAvailable =>
        IsEnvironmentFlagEnabled(Environment.GetEnvironmentVariable(ForceDockerUnavailableEnvironmentVariable))
            ? false
            : IsAvailableLazy.Value;

    /// <summary>
    /// Whether the caller explicitly requires Docker-backed coverage instead of the
    /// ordinary local graceful-skip contract.
    /// </summary>
    internal static bool IsDockerRequired =>
        IsEnvironmentFlagEnabled(Environment.GetEnvironmentVariable(RequireDockerEnvironmentVariable));

    /// <summary>
    /// Skip message for use with xUnit's Skip property.
    /// </summary>
    public const string SkipReason = "Docker is not available on this machine";

    /// <summary>
    /// Turns an unavailable Docker probe into a test failure when the caller has
    /// explicitly requested Docker-backed verification.
    /// </summary>
    internal static void EnsureRequiredDockerIsAvailable(bool isAvailable, bool dockerRequired)
    {
        if (dockerRequired && !isAvailable)
        {
            throw new InvalidOperationException(RequiredDockerFailureMessage);
        }
    }

    internal static bool IsEnvironmentFlagEnabled(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal) ||
        bool.TryParse(value, out var enabled) && enabled;

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
                    // Do NOT redirect stdout/stderr — reading them before
                    // WaitForExit is required to avoid a deadlock when the
                    // OS pipe buffer fills, and we don't need the output.
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            return CheckDocker(
                startProcess: () => process.Start(),
                waitForExit: process.WaitForExit,
                getExitCode: () => process.ExitCode,
                killProcessTree: () => process.Kill(entireProcessTree: true),
                probeTimeout: ProbeTimeout,
                reapTimeout: ReapTimeout);
        }
        catch
        {
            return false;
        }
    }

    internal static bool CheckDocker(
        Action startProcess,
        Func<TimeSpan, bool> waitForExit,
        Func<int> getExitCode,
        Action killProcessTree,
        TimeSpan probeTimeout,
        TimeSpan reapTimeout)
    {
        var started = false;
        var exited = false;

        try
        {
            startProcess();
            started = true;
            exited = waitForExit(probeTimeout);

            // ExitCode throws while the process is still running. A timeout is always
            // unavailable and is cleaned up in finally before this method returns.
            return exited && getExitCode() == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (started && !exited)
            {
                TerminateAndReap(killProcessTree, waitForExit, reapTimeout);
            }
        }
    }

    private static void TerminateAndReap(
        Action killProcessTree,
        Func<TimeSpan, bool> waitForExit,
        TimeSpan reapTimeout)
    {
        try
        {
            killProcessTree();
        }
        catch
        {
            // The probe may exit between the timed wait and Kill. Availability remains false.
        }

        try
        {
            _ = waitForExit(reapTimeout);
        }
        catch
        {
            // Cleanup is best-effort and must never turn unavailable Docker into a test failure.
        }
    }
}
