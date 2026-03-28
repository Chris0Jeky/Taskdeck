using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;

namespace Taskdeck.Cli.Commands;

internal sealed class ColumnsCommandHandler
{
    private readonly ColumnService _columnService;

    public ColumnsCommandHandler(ColumnService columnService)
    {
        _columnService = columnService;
    }

    public async Task<int> HandleAsync(string command, string[] args)
    {
        var outputJson = ArgParser.HasFlag(args, "--json");
        var normalizedArgs = ArgParser.StripFlag(args, "--json");

        return command switch
        {
            "list" => await ListAsync(normalizedArgs, outputJson),
            "create" => await CreateAsync(normalizedArgs, outputJson),
            _ => ConsoleOutput.PrintUsageError(
                $"Unknown columns command: '{command}'.",
                "taskdeck columns [list|create]")
        };
    }

    private async Task<int> ListAsync(string[] args, bool outputJson)
    {
        var boardIdText = ArgParser.GetOption(args, "--board");
        if (!ArgParser.TryParseGuid(boardIdText, out var boardId))
        {
            return ConsoleOutput.PrintUsageError(
                "Invalid or missing --board <board-id>.",
                "taskdeck columns list --board <board-id>");
        }

        var result = await _columnService.GetColumnsByBoardIdAsync(boardId);
        if (!result.IsSuccess)
        {
            return ConsoleOutput.PrintFailure(result.ErrorCode, result.ErrorMessage);
        }

        var columns = result.Value.ToList();
        if (columns.Count == 0)
        {
            if (outputJson)
            {
                ConsoleOutput.WriteJson(Array.Empty<ColumnDto>());
            }
            else
            {
                Console.WriteLine("No columns found.");
            }
            return ExitCodes.Success;
        }

        if (outputJson)
        {
            ConsoleOutput.WriteJson(columns);
            return ExitCodes.Success;
        }

        Console.WriteLine("Columns");
        foreach (var column in columns)
        {
            var wipText = column.WipLimit.HasValue ? $"WIP {column.WipLimit.Value}" : "WIP none";
            Console.WriteLine($"- {column.Id} | {column.Position} | {column.Name} | {wipText}");
        }

        return ExitCodes.Success;
    }

    private async Task<int> CreateAsync(string[] args, bool outputJson)
    {
        var boardIdText = ArgParser.GetOption(args, "--board");
        var name = ArgParser.GetOption(args, "--name");
        var positionText = ArgParser.GetOption(args, "--position");
        var wipText = ArgParser.GetOption(args, "--wip");

        if (!ArgParser.TryParseGuid(boardIdText, out var boardId))
        {
            return ConsoleOutput.PrintUsageError(
                "Invalid or missing --board <board-id>.",
                "taskdeck columns create --board <board-id> --name <name> [--position <position>] [--wip <limit>]");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return ConsoleOutput.PrintUsageError(
                "Missing --name <name>.",
                "taskdeck columns create --board <board-id> --name <name> [--position <position>] [--wip <limit>]");
        }

        int? position = null;
        if (positionText is not null)
        {
            if (!int.TryParse(positionText, out var parsedPosition) || parsedPosition < 0)
            {
                return ConsoleOutput.PrintUsageError(
                    "Invalid --position value. Position must be a non-negative integer.",
                    "taskdeck columns create --board <board-id> --name <name> [--position <position>] [--wip <limit>]");
            }

            position = parsedPosition;
        }

        int? wipLimit = null;
        if (wipText is not null)
        {
            if (!int.TryParse(wipText, out var parsedWip) || parsedWip <= 0)
            {
                return ConsoleOutput.PrintUsageError(
                    "Invalid --wip value. WIP limit must be greater than 0.",
                    "taskdeck columns create --board <board-id> --name <name> [--position <position>] [--wip <limit>]");
            }

            wipLimit = parsedWip;
        }

        var result = await _columnService.CreateColumnAsync(new CreateColumnDto(boardId, name, position, wipLimit));
        if (!result.IsSuccess)
        {
            return ConsoleOutput.PrintFailure(result.ErrorCode, result.ErrorMessage);
        }

        if (outputJson)
        {
            ConsoleOutput.WriteJson(result.Value);
        }
        else
        {
            var wip = result.Value.WipLimit.HasValue ? result.Value.WipLimit.Value.ToString() : "none";
            Console.WriteLine($"Created column: {result.Value.Id} | {result.Value.Name} | pos={result.Value.Position} | wip={wip}");
        }
        return ExitCodes.Success;
    }
}
