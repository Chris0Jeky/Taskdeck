using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Cli.Commands;

internal sealed class BoardsCommandHandler
{
    private readonly BoardService _boardService;
    private readonly IUnitOfWork _unitOfWork;

    public BoardsCommandHandler(BoardService boardService, IUnitOfWork unitOfWork)
    {
        _boardService = boardService;
        _unitOfWork = unitOfWork;
    }

    public async Task<int> HandleAsync(string command, string[] args)
    {
        var outputJson = ArgParser.HasFlag(args, "--json");
        var normalizedArgs = ArgParser.StripFlag(args, "--json");

        return command switch
        {
            "list" => await ListAsync(normalizedArgs, outputJson),
            "create" => await CreateAsync(normalizedArgs, outputJson),
            "update" => await UpdateAsync(normalizedArgs, outputJson),
            _ => ConsoleOutput.PrintUsageError(
                $"Unknown boards command: '{command}'.",
                "taskdeck boards [list|create|update]")
        };
    }

    private async Task<int> ListAsync(string[] args, bool outputJson)
    {
        var includeArchived = ArgParser.HasFlag(args, "--include-archived");
        var result = await _boardService.ListBoardsAsync(includeArchived: includeArchived);
        if (!result.IsSuccess)
        {
            return ConsoleOutput.PrintFailure(result.ErrorCode, result.ErrorMessage);
        }

        var boards = result.Value.ToList();
        if (boards.Count == 0)
        {
            if (outputJson)
            {
                ConsoleOutput.WriteJson(Array.Empty<BoardDto>());
            }
            else
            {
                Console.WriteLine("No boards found.");
            }
            return ExitCodes.Success;
        }

        if (outputJson)
        {
            ConsoleOutput.WriteJson(boards);
            return ExitCodes.Success;
        }

        Console.WriteLine("Boards");
        foreach (var board in boards)
        {
            var archivedMarker = board.IsArchived ? " [archived]" : string.Empty;
            Console.WriteLine($"- {board.Id} | {board.Name}{archivedMarker}");
        }

        return ExitCodes.Success;
    }

    private async Task<int> CreateAsync(string[] args, bool outputJson)
    {
        if (args.Length < 1)
        {
            return ConsoleOutput.PrintUsageError(
                "Missing board name.",
                "taskdeck boards create <name> [description]");
        }

        var name = args[0];
        var description = args.Length > 1 ? string.Join(' ', args.Skip(1)) : null;
        var actorId = await GetOrCreateCliActorIdAsync();
        var result = await _boardService.CreateBoardAsync(
            new CreateBoardDto(name, description),
            actorId);

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
            Console.WriteLine($"Created board: {result.Value.Id} | {result.Value.Name}");
        }
        return ExitCodes.Success;
    }

    private async Task<int> UpdateAsync(string[] args, bool outputJson)
    {
        var boardIdText = ArgParser.GetOption(args, "--board");
        if (!ArgParser.TryParseGuid(boardIdText, out var boardId))
        {
            return ConsoleOutput.PrintUsageError(
                "Invalid or missing --board <board-id>.",
                "taskdeck boards update --board <board-id> [--name <name>] [--description <description>] [--archive|--unarchive]");
        }

        var name = ArgParser.GetOption(args, "--name");
        var description = ArgParser.GetOption(args, "--description");
        var archiveFlag = ArgParser.HasFlag(args, "--archive");
        var unarchiveFlag = ArgParser.HasFlag(args, "--unarchive");

        if (archiveFlag && unarchiveFlag)
        {
            return ConsoleOutput.PrintUsageError(
                "Cannot use --archive and --unarchive together.",
                "taskdeck boards update --board <board-id> [--name <name>] [--description <description>] [--archive|--unarchive]");
        }

        bool? isArchived = archiveFlag ? true : unarchiveFlag ? false : null;

        if (name is null && description is null && isArchived is null)
        {
            return ConsoleOutput.PrintUsageError(
                "No update values supplied.",
                "taskdeck boards update --board <board-id> [--name <name>] [--description <description>] [--archive|--unarchive]");
        }

        var result = await _boardService.UpdateBoardAsync(boardId, new UpdateBoardDto(name, description, isArchived));
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
            var archivedMarker = result.Value.IsArchived ? "archived" : "active";
            Console.WriteLine($"Updated board: {result.Value.Id} | {result.Value.Name} | {archivedMarker}");
        }
        return ExitCodes.Success;
    }

    private async Task<Guid> GetOrCreateCliActorIdAsync()
    {
        const string actorUsername = "taskdeck_cli_actor";
        const string actorEmail = "taskdeck-cli-actor@local.taskdeck";

        var existingActor = await _unitOfWork.Users.GetByUsernameAsync(actorUsername);
        if (existingActor is not null)
        {
            return existingActor.Id;
        }

        var actor = new User(actorUsername, actorEmail, "cli-internal-actor-password-hash");
        await _unitOfWork.Users.AddAsync(actor);
        await _unitOfWork.SaveChangesAsync();
        return actor.Id;
    }
}
