using System.Text.Json;

namespace Taskdeck.Cli.Commands;

internal static class ConsoleOutput
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static int PrintUsageError(string message, string usage)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine($"Usage: {usage}");
        return ExitCodes.Usage;
    }

    public static int PrintFailure(string errorCode, string errorMessage)
    {
        Console.Error.WriteLine($"Error [{errorCode}]: {errorMessage}");
        return ExitCodes.Failure;
    }

    public static void WriteJson<T>(T value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            Taskdeck CLI

            Usage:
              taskdeck boards list [--include-archived]
              taskdeck boards create <name> [description]
              taskdeck boards update --board <board-id> [--name <name>] [--description <description>] [--archive|--unarchive]

              taskdeck columns list --board <board-id>
              taskdeck columns create --board <board-id> --name <name> [--position <position>] [--wip <limit>]

              taskdeck cards add --board <board-id> --column <column-id> --title <title> [--description <description>]
              taskdeck cards move --card <card-id> --target-column <column-id> [--position <position>]
              taskdeck cards list --board <board-id> [--search <text>] [--column <column-id>] [--label <label-id>]

              taskdeck api-key create --name <name> [--expires <days>]
              taskdeck api-key list
              taskdeck api-key revoke --name <name> | --id <key-id>

            Exit codes:
              0 success
              1 command failed
              2 usage error
            """);
    }
}
