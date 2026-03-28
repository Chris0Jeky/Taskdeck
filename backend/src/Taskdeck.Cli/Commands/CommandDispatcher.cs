using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Cli.Commands;

internal sealed class CommandDispatcher
{
    private readonly IServiceProvider _rootServices;

    public CommandDispatcher(IServiceProvider rootServices)
    {
        _rootServices = rootServices;
    }

    public async Task<int> DispatchAsync(string[] args)
    {
        if (args.Length == 0)
        {
            ConsoleOutput.PrintHelp();
            return ExitCodes.Usage;
        }

        using var scope = _rootServices.CreateScope();
        var group = args[0].ToLowerInvariant();
        var command = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
        var remainingArgs = args.Skip(2).ToArray();

        return group switch
        {
            "boards" => await CreateBoardsHandler(scope).HandleAsync(command, remainingArgs),
            "columns" => await CreateColumnsHandler(scope).HandleAsync(command, remainingArgs),
            "cards" => await CreateCardsHandler(scope).HandleAsync(command, remainingArgs),
            "help" => ReturnHelp(),
            _ => ReturnUnknownCommand(group)
        };
    }

    private static BoardsCommandHandler CreateBoardsHandler(IServiceScope scope)
    {
        var boardService = scope.ServiceProvider.GetRequiredService<BoardService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        return new BoardsCommandHandler(boardService, unitOfWork);
    }

    private static ColumnsCommandHandler CreateColumnsHandler(IServiceScope scope)
    {
        var columnService = scope.ServiceProvider.GetRequiredService<ColumnService>();
        return new ColumnsCommandHandler(columnService);
    }

    private static CardsCommandHandler CreateCardsHandler(IServiceScope scope)
    {
        var cardService = scope.ServiceProvider.GetRequiredService<CardService>();
        return new CardsCommandHandler(cardService);
    }

    private static int ReturnHelp()
    {
        ConsoleOutput.PrintHelp();
        return ExitCodes.Success;
    }

    private static int ReturnUnknownCommand(string commandGroup)
    {
        Console.Error.WriteLine($"Unknown command group: '{commandGroup}'.");
        ConsoleOutput.PrintHelp();
        return ExitCodes.Usage;
    }
}
