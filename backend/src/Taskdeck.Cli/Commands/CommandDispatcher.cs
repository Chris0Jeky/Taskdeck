using Microsoft.Extensions.DependencyInjection;

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
            "boards" => await scope.ServiceProvider.GetRequiredService<BoardsCommandHandler>().HandleAsync(command, remainingArgs),
            "columns" => await scope.ServiceProvider.GetRequiredService<ColumnsCommandHandler>().HandleAsync(command, remainingArgs),
            "cards" => await scope.ServiceProvider.GetRequiredService<CardsCommandHandler>().HandleAsync(command, remainingArgs),
            "help" => ReturnHelp(),
            _ => ReturnUnknownCommand(group)
        };
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
