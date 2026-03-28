using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;

namespace Taskdeck.Cli.Commands;

internal sealed class CardsCommandHandler
{
    private readonly CardService _cardService;

    public CardsCommandHandler(CardService cardService)
    {
        _cardService = cardService;
    }

    public async Task<int> HandleAsync(string command, string[] args)
    {
        var outputJson = ArgParser.HasFlag(args, "--json");
        var normalizedArgs = ArgParser.StripFlag(args, "--json");

        return command switch
        {
            "add" => await AddAsync(normalizedArgs, outputJson),
            "move" => await MoveAsync(normalizedArgs, outputJson),
            "list" => await ListAsync(normalizedArgs, outputJson),
            _ => ConsoleOutput.PrintUsageError(
                $"Unknown cards command: '{command}'.",
                "taskdeck cards [add|move|list]")
        };
    }

    private async Task<int> AddAsync(string[] args, bool outputJson)
    {
        var boardIdText = ArgParser.GetOption(args, "--board");
        var columnIdText = ArgParser.GetOption(args, "--column");
        var title = ArgParser.GetOption(args, "--title");
        var description = ArgParser.GetOption(args, "--description");

        if (!ArgParser.TryParseGuid(boardIdText, out var boardId))
        {
            return ConsoleOutput.PrintUsageError(
                "Invalid or missing --board <board-id>.",
                "taskdeck cards add --board <board-id> --column <column-id> --title <title> [--description <description>]");
        }

        if (!ArgParser.TryParseGuid(columnIdText, out var columnId))
        {
            return ConsoleOutput.PrintUsageError(
                "Invalid or missing --column <column-id>.",
                "taskdeck cards add --board <board-id> --column <column-id> --title <title> [--description <description>]");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return ConsoleOutput.PrintUsageError(
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

        var result = await _cardService.CreateCardAsync(createRequest);
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
            Console.WriteLine($"Created card: {result.Value.Id} | {result.Value.Title}");
        }
        return ExitCodes.Success;
    }

    private async Task<int> MoveAsync(string[] args, bool outputJson)
    {
        var cardIdText = ArgParser.GetOption(args, "--card");
        var targetColumnIdText = ArgParser.GetOption(args, "--target-column");
        var positionText = ArgParser.GetOption(args, "--position") ?? "0";

        if (!ArgParser.TryParseGuid(cardIdText, out var cardId))
        {
            return ConsoleOutput.PrintUsageError(
                "Invalid or missing --card <card-id>.",
                "taskdeck cards move --card <card-id> --target-column <column-id> [--position <position>]");
        }

        if (!ArgParser.TryParseGuid(targetColumnIdText, out var targetColumnId))
        {
            return ConsoleOutput.PrintUsageError(
                "Invalid or missing --target-column <column-id>.",
                "taskdeck cards move --card <card-id> --target-column <column-id> [--position <position>]");
        }

        if (!int.TryParse(positionText, out var position) || position < 0)
        {
            return ConsoleOutput.PrintUsageError(
                "Invalid --position value. Position must be a non-negative integer.",
                "taskdeck cards move --card <card-id> --target-column <column-id> [--position <position>]");
        }

        var result = await _cardService.MoveCardAsync(cardId, new MoveCardDto(targetColumnId, position));
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
            Console.WriteLine($"Moved card: {result.Value.Id} -> column {result.Value.ColumnId} @ position {result.Value.Position}");
        }
        return ExitCodes.Success;
    }

    private async Task<int> ListAsync(string[] args, bool outputJson)
    {
        var boardIdText = ArgParser.GetOption(args, "--board");
        var search = ArgParser.GetOption(args, "--search");
        var columnIdText = ArgParser.GetOption(args, "--column");
        var labelIdText = ArgParser.GetOption(args, "--label");

        if (!ArgParser.TryParseGuid(boardIdText, out var boardId))
        {
            return ConsoleOutput.PrintUsageError(
                "Invalid or missing --board <board-id>.",
                "taskdeck cards list --board <board-id> [--search <text>] [--column <column-id>] [--label <label-id>]");
        }

        Guid? columnId = null;
        if (columnIdText is not null)
        {
            if (!ArgParser.TryParseGuid(columnIdText, out var parsedColumnId))
            {
                return ConsoleOutput.PrintUsageError(
                    "Invalid --column <column-id>.",
                    "taskdeck cards list --board <board-id> [--search <text>] [--column <column-id>] [--label <label-id>]");
            }

            columnId = parsedColumnId;
        }

        Guid? labelId = null;
        if (labelIdText is not null)
        {
            if (!ArgParser.TryParseGuid(labelIdText, out var parsedLabelId))
            {
                return ConsoleOutput.PrintUsageError(
                    "Invalid --label <label-id>.",
                    "taskdeck cards list --board <board-id> [--search <text>] [--column <column-id>] [--label <label-id>]");
            }

            labelId = parsedLabelId;
        }

        var result = await _cardService.SearchCardsAsync(boardId, search, labelId, columnId);
        if (!result.IsSuccess)
        {
            return ConsoleOutput.PrintFailure(result.ErrorCode, result.ErrorMessage);
        }

        var cards = result.Value
            .OrderBy(c => c.ColumnId)
            .ThenBy(c => c.Position)
            .ToList();

        if (cards.Count == 0)
        {
            if (outputJson)
            {
                ConsoleOutput.WriteJson(Array.Empty<CardDto>());
            }
            else
            {
                Console.WriteLine("No cards found.");
            }
            return ExitCodes.Success;
        }

        if (outputJson)
        {
            ConsoleOutput.WriteJson(cards);
            return ExitCodes.Success;
        }

        Console.WriteLine("Cards");
        foreach (var card in cards)
        {
            var blocked = card.IsBlocked ? " [blocked]" : string.Empty;
            Console.WriteLine($"- {card.Id} | column={card.ColumnId} pos={card.Position} | {card.Title}{blocked}");
        }

        return ExitCodes.Success;
    }
}
