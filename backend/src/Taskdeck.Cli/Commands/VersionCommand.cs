using Taskdeck.Application.Common;

namespace Taskdeck.Cli.Commands;

/// <summary>
/// Handles <c>taskdeck --version</c> (and the bare <c>version</c> group).
/// </summary>
/// <remarks>
/// Intercepted in <c>Program.cs</c> before the host is built, so asking for the version
/// never touches configuration, the database, or migrations — a self-hoster can answer
/// "what version am I running?" on a machine whose data directory is broken or absent.
/// Output stays clean JSON like every other CLI command.
/// </remarks>
internal static class VersionCommand
{
    /// <summary>
    /// True when the first argument requests the version. Trailing arguments are ignored,
    /// matching the usual <c>--version</c> convention.
    /// </summary>
    public static bool IsVersionRequest(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return false;
        }

        return string.Equals(args[0], "--version", StringComparison.OrdinalIgnoreCase)
               || string.Equals(args[0], "version", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Writes the stamped product version as JSON and returns the success exit code.</summary>
    public static int Execute()
    {
        ConsoleOutput.WriteJson(new { version = ProductVersion.Value });
        return ExitCodes.Success;
    }
}
