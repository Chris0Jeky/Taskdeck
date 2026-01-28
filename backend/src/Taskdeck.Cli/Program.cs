using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Infrastructure;
using Taskdeck.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);

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

static async Task<int> RunAsync(IServiceProvider rootServices, string[] args)
{
    if (args.Length == 0)
    {
        PrintHelp();
        return 1;
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

static async Task<int> HandleBoardsAsync(BoardService boardService, string command, string[] args)
{
    switch (command)
    {
        case "list":
        {
            var includeArchived = HasFlag(args, "--include-archived");
            var result = await boardService.ListBoardsAsync(includeArchived: includeArchived);
            if (!result.IsSuccess)
            {
                return PrintFailure(result.ErrorCode, result.ErrorMessage);
            }

            Console.WriteLine("Boards");
            foreach (var board in result.Value)
            {
                var archivedMarker = board.IsArchived ? " [archived]" : string.Empty;
                Console.WriteLine($"- {board.Id} | {board.Name}{archivedMarker}");
            }

            return 0;
        }
        case "create":
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("Missing board name.");
                Console.Error.WriteLine("Usage: taskdeck boards create <name> [description]");
                return 1;
            }

            var name = args[0];
            var description = args.Length > 1 ? string.Join(' ', args.Skip(1)) : null;
            var result = await boardService.CreateBoardAsync(new CreateBoardDto(name, description));

            if (!result.IsSuccess)
            {
                return PrintFailure(result.ErrorCode, result.ErrorMessage);
            }

            Console.WriteLine($"Created board: {result.Value.Id} | {result.Value.Name}");
            return 0;
        }
        default:
            Console.Error.WriteLine($"Unknown boards command: '{command}'.");
            Console.Error.WriteLine("Usage: taskdeck boards [list|create]");
            return 1;
    }
}

static async Task<int> HandleColumnsAsync(ColumnService columnService, string command, string[] args)
{
    if (command != "list")
    {
        Console.Error.WriteLine($"Unknown columns command: '{command}'.");
        Console.Error.WriteLine("Usage: taskdeck columns list --board <board-id>");
        return 1;
    }

    var boardIdText = GetOption(args, "--board");
    if (!TryParseGuid(boardIdText, out var boardId))
    {
        Console.Error.WriteLine("Invalid or missing --board <board-id>.");
        return 1;
    }

    var result = await columnService.GetColumnsByBoardIdAsync(boardId);
    if (!result.IsSuccess)
    {
        return PrintFailure(result.ErrorCode, result.ErrorMessage);
    }

    Console.WriteLine("Columns");
    foreach (var column in result.Value)
    {
        var wipText = column.WipLimit.HasValue ? $"WIP {column.WipLimit.Value}" : "WIP none";
        Console.WriteLine($"- {column.Id} | {column.Position} | {column.Name} | {wipText}");
    }

    return 0;
}

static async Task<int> HandleCardsAsync(CardService cardService, string command, string[] args)
{
    switch (command)
    {
        case "add":
        {
            var boardIdText = GetOption(args, "--board");
            var columnIdText = GetOption(args, "--column");
            var title = GetOption(args, "--title");
            var description = GetOption(args, "--description");

            if (!TryParseGuid(boardIdText, out var boardId))
            {
                Console.Error.WriteLine("Invalid or missing --board <board-id>.");
                return 1;
            }

            if (!TryParseGuid(columnIdText, out var columnId))
            {
                Console.Error.WriteLine("Invalid or missing --column <column-id>.");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                Console.Error.WriteLine("Missing --title <title>.");
                return 1;
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

            Console.WriteLine($"Created card: {result.Value.Id} | {result.Value.Title}");
            return 0;
        }
        case "move":
        {
            var cardIdText = GetOption(args, "--card");
            var targetColumnIdText = GetOption(args, "--target-column");
            var positionText = GetOption(args, "--position") ?? "0";

            if (!TryParseGuid(cardIdText, out var cardId))
            {
                Console.Error.WriteLine("Invalid or missing --card <card-id>.");
                return 1;
            }

            if (!TryParseGuid(targetColumnIdText, out var targetColumnId))
            {
                Console.Error.WriteLine("Invalid or missing --target-column <column-id>.");
                return 1;
            }

            if (!int.TryParse(positionText, out var position) || position < 0)
            {
                Console.Error.WriteLine("Invalid --position value. Position must be a non-negative integer.");
                return 1;
            }

            var result = await cardService.MoveCardAsync(cardId, new MoveCardDto(targetColumnId, position));
            if (!result.IsSuccess)
            {
                return PrintFailure(result.ErrorCode, result.ErrorMessage);
            }

            Console.WriteLine($"Moved card: {result.Value.Id} -> column {result.Value.ColumnId} @ position {result.Value.Position}");
            return 0;
        }
        default:
            Console.Error.WriteLine($"Unknown cards command: '{command}'.");
            Console.Error.WriteLine("Usage: taskdeck cards [add|move]");
            return 1;
    }
}

static bool HasFlag(IReadOnlyList<string> args, string optionName)
{
    return args.Any(arg => string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase));
}

static string? GetOption(IReadOnlyList<string> args, string optionName)
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

static bool TryParseGuid(string? text, out Guid value)
{
    return Guid.TryParse(text, out value);
}

static int PrintFailure(string errorCode, string errorMessage)
{
    Console.Error.WriteLine($"Error [{errorCode}]: {errorMessage}");
    return 1;
}

static int ReturnUnknownCommand(string commandGroup)
{
    Console.Error.WriteLine($"Unknown command group: '{commandGroup}'.");
    PrintHelp();
    return 1;
}

static int ReturnHelp()
{
    PrintHelp();
    return 0;
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        Taskdeck CLI (Phase 4 bootstrap)

        Usage:
          taskdeck boards list [--include-archived]
          taskdeck boards create <name> [description]
          taskdeck columns list --board <board-id>
          taskdeck cards add --board <board-id> --column <column-id> --title <title> [--description <description>]
          taskdeck cards move --card <card-id> --target-column <column-id> [--position <position>]
        """);
}
