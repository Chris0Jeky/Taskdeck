using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Infrastructure;
using Taskdeck.Infrastructure.Persistence;

const int ExitSuccess = 0;
const int ExitFailure = 1;
const int ExitUsage = 2;
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = false,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

var fallbackConnectionString = Environment.GetEnvironmentVariable("TASKDECK_CONNECTION_STRING")
    ?? "Data Source=taskdeck.db";

if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DefaultConnection")))
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = fallbackConnectionString
    });
}

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<BoardService>();
builder.Services.AddScoped<ColumnService>();
builder.Services.AddScoped<CardService>();

using var host = builder.Build();

using (var startupScope = host.Services.CreateScope())
{
    var dbContext = startupScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
    dbContext.Database.Migrate();
}

return await RunAsync(host.Services, args);

async Task<int> RunAsync(IServiceProvider rootServices, string[] args)
{
    if (args.Length == 0)
    {
        PrintHelp();
        return ExitUsage;
    }

    using var scope = rootServices.CreateScope();
    var boardService = scope.ServiceProvider.GetRequiredService<BoardService>();
    var columnService = scope.ServiceProvider.GetRequiredService<ColumnService>();
    var cardService = scope.ServiceProvider.GetRequiredService<CardService>();

    var group = args[0].ToLowerInvariant();
    var command = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;

    return group switch
    {
        "boards" => await HandleBoardsAsync(boardService, command, args.Skip(2).ToArray()),
        "columns" => await HandleColumnsAsync(columnService, command, args.Skip(2).ToArray()),
        "cards" => await HandleCardsAsync(cardService, command, args.Skip(2).ToArray()),
        "help" => ReturnHelp(),
        _ => ReturnUnknownCommand(group)
    };
}

async Task<int> HandleBoardsAsync(BoardService boardService, string command, string[] args)
{
    var outputJson = HasFlag(args, "--json");
    var normalizedArgs = args
        .Where(arg => !string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    switch (command)
    {
        case "list":
        {
            var includeArchived = HasFlag(normalizedArgs, "--include-archived");
            var result = await boardService.ListBoardsAsync(includeArchived: includeArchived);
            if (!result.IsSuccess)
            {
                return PrintFailure(result.ErrorCode, result.ErrorMessage);
            }

            var boards = result.Value.ToList();
            if (boards.Count == 0)
            {
                if (outputJson)
                {
                    WriteJson(Array.Empty<BoardDto>(), jsonOptions);
                }
                else
                {
                    Console.WriteLine("No boards found.");
                }
                return ExitSuccess;
            }

            if (outputJson)
            {
                WriteJson(boards, jsonOptions);
                return ExitSuccess;
            }

            Console.WriteLine("Boards");
            foreach (var board in boards)
            {
                var archivedMarker = board.IsArchived ? " [archived]" : string.Empty;
                Console.WriteLine($"- {board.Id} | {board.Name}{archivedMarker}");
            }

            return ExitSuccess;
        }
        case "create":
        {
            if (normalizedArgs.Length < 1)
            {
                return PrintUsageError(
                    "Missing board name.",
                    "taskdeck boards create <name> [description]");
            }

            var name = normalizedArgs[0];
            var description = normalizedArgs.Length > 1 ? string.Join(' ', normalizedArgs.Skip(1)) : null;
            var result = await boardService.CreateBoardAsync(
                new CreateBoardDto(name, description),
                GetCliActorId());

            if (!result.IsSuccess)
            {
                return PrintFailure(result.ErrorCode, result.ErrorMessage);
            }

            if (outputJson)
            {
                WriteJson(result.Value, jsonOptions);
            }
            else
            {
                Console.WriteLine($"Created board: {result.Value.Id} | {result.Value.Name}");
            }
            return ExitSuccess;
        }
        case "update":
        {
            var boardIdText = GetOption(normalizedArgs, "--board");
            if (!TryParseGuid(boardIdText, out var boardId))
            {
                return PrintUsageError(
                    "Invalid or missing --board <board-id>.",
                    "taskdeck boards update --board <board-id> [--name <name>] [--description <description>] [--archive|--unarchive]");
            }

            var name = GetOption(normalizedArgs, "--name");
            var description = GetOption(normalizedArgs, "--description");
            var archiveFlag = HasFlag(normalizedArgs, "--archive");
            var unarchiveFlag = HasFlag(normalizedArgs, "--unarchive");

            if (archiveFlag && unarchiveFlag)
            {
                return PrintUsageError(
                    "Cannot use --archive and --unarchive together.",
                    "taskdeck boards update --board <board-id> [--name <name>] [--description <description>] [--archive|--unarchive]");
            }

            bool? isArchived = archiveFlag ? true : unarchiveFlag ? false : null;

            if (name is null && description is null && isArchived is null)
            {
                return PrintUsageError(
                    "No update values supplied.",
                    "taskdeck boards update --board <board-id> [--name <name>] [--description <description>] [--archive|--unarchive]");
            }

            var result = await boardService.UpdateBoardAsync(boardId, new UpdateBoardDto(name, description, isArchived));
            if (!result.IsSuccess)
            {
                return PrintFailure(result.ErrorCode, result.ErrorMessage);
            }

            if (outputJson)
            {
                WriteJson(result.Value, jsonOptions);
            }
            else
            {
                var archivedMarker = result.Value.IsArchived ? "archived" : "active";
                Console.WriteLine($"Updated board: {result.Value.Id} | {result.Value.Name} | {archivedMarker}");
            }
            return ExitSuccess;
        }
        default:
            return PrintUsageError(
                $"Unknown boards command: '{command}'.",
                "taskdeck boards [list|create|update]");
    }
}

async Task<int> HandleColumnsAsync(ColumnService columnService, string command, string[] args)
{
    var outputJson = HasFlag(args, "--json");
    var normalizedArgs = args
        .Where(arg => !string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    switch (command)
    {
        case "list":
        {
            var boardIdText = GetOption(normalizedArgs, "--board");
            if (!TryParseGuid(boardIdText, out var boardId))
            {
                return PrintUsageError(
                    "Invalid or missing --board <board-id>.",
                    "taskdeck columns list --board <board-id>");
            }

            var result = await columnService.GetColumnsByBoardIdAsync(boardId);
            if (!result.IsSuccess)
            {
                return PrintFailure(result.ErrorCode, result.ErrorMessage);
            }

            var columns = result.Value.ToList();
            if (columns.Count == 0)
            {
                if (outputJson)
                {
                    WriteJson(Array.Empty<ColumnDto>(), jsonOptions);
                }
                else
                {
                    Console.WriteLine("No columns found.");
                }
                return ExitSuccess;
            }

            if (outputJson)
            {
                WriteJson(columns, jsonOptions);
                return ExitSuccess;
            }

            Console.WriteLine("Columns");
            foreach (var column in columns)
            {
                var wipText = column.WipLimit.HasValue ? $"WIP {column.WipLimit.Value}" : "WIP none";
                Console.WriteLine($"- {column.Id} | {column.Position} | {column.Name} | {wipText}");
            }

            return ExitSuccess;
        }
        case "create":
        {
            var boardIdText = GetOption(normalizedArgs, "--board");
            var name = GetOption(normalizedArgs, "--name");
            var positionText = GetOption(normalizedArgs, "--position");
            var wipText = GetOption(normalizedArgs, "--wip");

            if (!TryParseGuid(boardIdText, out var boardId))
            {
                return PrintUsageError(
                    "Invalid or missing --board <board-id>.",
                    "taskdeck columns create --board <board-id> --name <name> [--position <position>] [--wip <limit>]");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return PrintUsageError(
                    "Missing --name <name>.",
                    "taskdeck columns create --board <board-id> --name <name> [--position <position>] [--wip <limit>]");
            }

            int? position = null;
            if (positionText is not null)
            {
                if (!int.TryParse(positionText, out var parsedPosition) || parsedPosition < 0)
                {
                    return PrintUsageError(
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
                    return PrintUsageError(
                        "Invalid --wip value. WIP limit must be greater than 0.",
                        "taskdeck columns create --board <board-id> --name <name> [--position <position>] [--wip <limit>]");
                }

                wipLimit = parsedWip;
            }

            var result = await columnService.CreateColumnAsync(new CreateColumnDto(boardId, name, position, wipLimit));
            if (!result.IsSuccess)
            {
                return PrintFailure(result.ErrorCode, result.ErrorMessage);
            }

            if (outputJson)
            {
                WriteJson(result.Value, jsonOptions);
            }
            else
            {
                var wip = result.Value.WipLimit.HasValue ? result.Value.WipLimit.Value.ToString() : "none";
                Console.WriteLine($"Created column: {result.Value.Id} | {result.Value.Name} | pos={result.Value.Position} | wip={wip}");
            }
            return ExitSuccess;
        }
        default:
            return PrintUsageError(
                $"Unknown columns command: '{command}'.",
                "taskdeck columns [list|create]");
    }
}

async Task<int> HandleCardsAsync(CardService cardService, string command, string[] args)
{
    var outputJson = HasFlag(args, "--json");
    var normalizedArgs = args
        .Where(arg => !string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    switch (command)
    {
        case "add":
        {
            var boardIdText = GetOption(normalizedArgs, "--board");
            var columnIdText = GetOption(normalizedArgs, "--column");
            var title = GetOption(normalizedArgs, "--title");
            var description = GetOption(normalizedArgs, "--description");

            if (!TryParseGuid(boardIdText, out var boardId))
            {
                return PrintUsageError(
                    "Invalid or missing --board <board-id>.",
                    "taskdeck cards add --board <board-id> --column <column-id> --title <title> [--description <description>]");
            }

            if (!TryParseGuid(columnIdText, out var columnId))
            {
                return PrintUsageError(
                    "Invalid or missing --column <column-id>.",
                    "taskdeck cards add --board <board-id> --column <column-id> --title <title> [--description <description>]");
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return PrintUsageError(
                    "Missing --title <title>.",
                    "taskdeck cards add --board <board-id> --column <column-id> --title <title> [--description <description>]");
            }

            var createRequest = new CreateCardDto(
                BoardId: boardId,
                ColumnId: columnId,
                Title: title,
                Description: description,
                DueDate: null,
                LabelIds: null);

            var result = await cardService.CreateCardAsync(createRequest);
            if (!result.IsSuccess)
            {
                return PrintFailure(result.ErrorCode, result.ErrorMessage);
            }

            if (outputJson)
            {
                WriteJson(result.Value, jsonOptions);
            }
            else
            {
                Console.WriteLine($"Created card: {result.Value.Id} | {result.Value.Title}");
            }
            return ExitSuccess;
        }
        case "move":
        {
            var cardIdText = GetOption(normalizedArgs, "--card");
            var targetColumnIdText = GetOption(normalizedArgs, "--target-column");
            var positionText = GetOption(normalizedArgs, "--position") ?? "0";

            if (!TryParseGuid(cardIdText, out var cardId))
            {
                return PrintUsageError(
                    "Invalid or missing --card <card-id>.",
                    "taskdeck cards move --card <card-id> --target-column <column-id> [--position <position>]");
            }

            if (!TryParseGuid(targetColumnIdText, out var targetColumnId))
            {
                return PrintUsageError(
                    "Invalid or missing --target-column <column-id>.",
                    "taskdeck cards move --card <card-id> --target-column <column-id> [--position <position>]");
            }

            if (!int.TryParse(positionText, out var position) || position < 0)
            {
                return PrintUsageError(
                    "Invalid --position value. Position must be a non-negative integer.",
                    "taskdeck cards move --card <card-id> --target-column <column-id> [--position <position>]");
            }

            var result = await cardService.MoveCardAsync(cardId, new MoveCardDto(targetColumnId, position));
            if (!result.IsSuccess)
            {
                return PrintFailure(result.ErrorCode, result.ErrorMessage);
            }

            if (outputJson)
            {
                WriteJson(result.Value, jsonOptions);
            }
            else
            {
                Console.WriteLine($"Moved card: {result.Value.Id} -> column {result.Value.ColumnId} @ position {result.Value.Position}");
            }
            return ExitSuccess;
        }
        case "list":
        {
            var boardIdText = GetOption(normalizedArgs, "--board");
            var search = GetOption(normalizedArgs, "--search");
            var columnIdText = GetOption(normalizedArgs, "--column");
            var labelIdText = GetOption(normalizedArgs, "--label");

            if (!TryParseGuid(boardIdText, out var boardId))
            {
                return PrintUsageError(
                    "Invalid or missing --board <board-id>.",
                    "taskdeck cards list --board <board-id> [--search <text>] [--column <column-id>] [--label <label-id>]");
            }

            Guid? columnId = null;
            if (columnIdText is not null)
            {
                if (!TryParseGuid(columnIdText, out var parsedColumnId))
                {
                    return PrintUsageError(
                        "Invalid --column <column-id>.",
                        "taskdeck cards list --board <board-id> [--search <text>] [--column <column-id>] [--label <label-id>]");
                }

                columnId = parsedColumnId;
            }

            Guid? labelId = null;
            if (labelIdText is not null)
            {
                if (!TryParseGuid(labelIdText, out var parsedLabelId))
                {
                    return PrintUsageError(
                        "Invalid --label <label-id>.",
                        "taskdeck cards list --board <board-id> [--search <text>] [--column <column-id>] [--label <label-id>]");
                }

                labelId = parsedLabelId;
            }

            var result = await cardService.SearchCardsAsync(boardId, search, labelId, columnId);
            if (!result.IsSuccess)
            {
                return PrintFailure(result.ErrorCode, result.ErrorMessage);
            }

            var cards = result.Value
                .OrderBy(c => c.ColumnId)
                .ThenBy(c => c.Position)
                .ToList();

            if (cards.Count == 0)
            {
                if (outputJson)
                {
                    WriteJson(Array.Empty<CardDto>(), jsonOptions);
                }
                else
                {
                    Console.WriteLine("No cards found.");
                }
                return ExitSuccess;
            }

            if (outputJson)
            {
                WriteJson(cards, jsonOptions);
                return ExitSuccess;
            }

            Console.WriteLine("Cards");
            foreach (var card in cards)
            {
                var blocked = card.IsBlocked ? " [blocked]" : string.Empty;
                Console.WriteLine($"- {card.Id} | column={card.ColumnId} pos={card.Position} | {card.Title}{blocked}");
            }

            return ExitSuccess;
        }
        default:
            return PrintUsageError(
                $"Unknown cards command: '{command}'.",
                "taskdeck cards [add|move|list]");
    }
}

bool HasFlag(IReadOnlyList<string> args, string optionName)
{
    return args.Any(arg => string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase));
}

string? GetOption(IReadOnlyList<string> args, string optionName)
{
    for (var i = 0; i < args.Count - 1; i++)
    {
        if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

bool TryParseGuid(string? text, out Guid value)
{
    return Guid.TryParse(text, out value);
}

int PrintUsageError(string message, string usage)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine($"Usage: {usage}");
    return ExitUsage;
}

int PrintFailure(string errorCode, string errorMessage)
{
    Console.Error.WriteLine($"Error [{errorCode}]: {errorMessage}");
    return ExitFailure;
}

int ReturnUnknownCommand(string commandGroup)
{
    Console.Error.WriteLine($"Unknown command group: '{commandGroup}'.");
    PrintHelp();
    return ExitUsage;
}

int ReturnHelp()
{
    PrintHelp();
    return ExitSuccess;
}

void PrintHelp()
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

        Exit codes:
          0 success
          1 command failed
          2 usage error
        """);
}

void WriteJson<T>(T value, JsonSerializerOptions options)
{
    Console.WriteLine(JsonSerializer.Serialize(value, options));
}

Guid GetCliActorId()
{
    var configuredActorId = Environment.GetEnvironmentVariable("TASKDECK_CLI_ACTOR_ID");
    if (Guid.TryParse(configuredActorId, out var actorId) && actorId != Guid.Empty)
    {
        return actorId;
    }

    return Guid.Parse("11111111-1111-1111-1111-111111111111");
}
