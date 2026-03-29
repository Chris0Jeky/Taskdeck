using System.Text.Json;
using System.Text.RegularExpressions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class AutomationPlannerService : IAutomationPlannerService
{
    private const int MaxProposalMetadataLength = 100;
    private readonly IAutomationProposalService _proposalService;
    private readonly IAutomationPolicyEngine _policyEngine;
    private readonly IUnitOfWork _unitOfWork;

    public AutomationPlannerService(
        IAutomationProposalService proposalService,
        IAutomationPolicyEngine policyEngine,
        IUnitOfWork unitOfWork)
    {
        _proposalService = proposalService;
        _policyEngine = policyEngine;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ProposalDto>> ParseInstructionAsync(
        string instruction,
        Guid userId,
        Guid? boardId = null,
        CancellationToken cancellationToken = default,
        ProposalSourceType sourceType = ProposalSourceType.Manual,
        string? sourceReferenceId = null,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(instruction))
            return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, "Instruction cannot be empty");

        if (userId == Guid.Empty)
            return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, "UserId cannot be empty");

        if (!TryResolveCorrelationId(correlationId, out var resolvedCorrelationId, out var correlationError))
        {
            return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, correlationError);
        }

        if (!TryResolveSourceReferenceId(sourceReferenceId, out var normalizedSourceReferenceId, out var sourceReferenceError))
        {
            return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, sourceReferenceError);
        }

        try
        {
            var operations = new List<CreateProposalOperationDto>();
            var sequence = 0;

            // Pattern: "create card 'title' in column 'column name'" or "create card 'title'"
            var createCardMatch = Regex.Match(
                instruction,
                @"^\s*create card ['""]([^'""]+)['""](?:\s+in column ['""]([^'""]+)['""])?(?:\s+with description ['""]([^'""]+)['""])?\s*$",
                RegexOptions.IgnoreCase);
            if (createCardMatch.Success)
            {
                var title = createCardMatch.Groups[1].Value;
                var columnName = createCardMatch.Groups.Count > 2 ? createCardMatch.Groups[2].Value : null;
                var description = createCardMatch.Groups.Count > 3 ? createCardMatch.Groups[3].Value : null;

                if (!boardId.HasValue)
                    return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, "Board ID is required for card operations");

                // Find column ID if column name is specified
                Guid? columnId = null;
                if (!string.IsNullOrEmpty(columnName))
                {
                    var columns = await _unitOfWork.Columns.GetByBoardIdAsync(boardId.Value, cancellationToken);
                    var column = columns.FirstOrDefault(c => c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
                    if (column == null)
                        return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Column '{columnName}' not found in board");
                    
                    columnId = column.Id;
                }
                else
                {
                    // Use first column as default
                    var columns = await _unitOfWork.Columns.GetByBoardIdAsync(boardId.Value, cancellationToken);
                    var firstColumn = columns.OrderBy(c => c.Position).FirstOrDefault();
                    if (firstColumn == null)
                        return Result.Failure<ProposalDto>(ErrorCodes.NotFound, "No columns found in board");
                    
                    columnId = firstColumn.Id;
                }

                var parameters = JsonSerializer.Serialize(new
                {
                    title,
                    description,
                    columnId,
                    boardId
                });

                operations.Add(new CreateProposalOperationDto(
                    sequence++,
                    "create",
                    "card",
                    parameters,
                    Guid.NewGuid().ToString()
                ));
            }
            else
            {
                // Pattern: "move card {id} to column 'column name'"
                var moveCardMatch = Regex.Match(
                    instruction,
                    @"^\s*move card ([a-f0-9-]+) to column ['""]([^'""]+)['""]\s*$",
                    RegexOptions.IgnoreCase);
                if (moveCardMatch.Success)
                {
                    var cardIdStr = moveCardMatch.Groups[1].Value;
                    var columnName = moveCardMatch.Groups[2].Value;

                    if (!Guid.TryParse(cardIdStr, out var cardId))
                        return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, $"Invalid card ID: {cardIdStr}");

                    if (!boardId.HasValue)
                        return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, "Board ID is required for card operations");

                    // Find column
                    var columns = await _unitOfWork.Columns.GetByBoardIdAsync(boardId.Value, cancellationToken);
                    var column = columns.FirstOrDefault(c => c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
                    if (column == null)
                        return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Column '{columnName}' not found in board");

                    var parameters = JsonSerializer.Serialize(new
                    {
                        cardId,
                        columnId = column.Id
                    });

                    operations.Add(new CreateProposalOperationDto(
                        sequence++,
                        "move",
                        "card",
                        parameters,
                        Guid.NewGuid().ToString(),
                        TargetId: cardId.ToString()
                    ));
                }
                else
                {
                    // Pattern: "archive card {id}"
                    var archiveCardMatch = Regex.Match(
                        instruction,
                        @"^\s*archive card ([a-f0-9-]+)\s*$",
                        RegexOptions.IgnoreCase);
                    if (archiveCardMatch.Success)
                    {
                        var cardIdStr = archiveCardMatch.Groups[1].Value;
                        if (!Guid.TryParse(cardIdStr, out var cardId))
                            return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, $"Invalid card ID: {cardIdStr}");

                        var parameters = JsonSerializer.Serialize(new { cardId });

                        operations.Add(new CreateProposalOperationDto(
                            sequence++,
                            "archive",
                            "card",
                            parameters,
                            Guid.NewGuid().ToString(),
                            TargetId: cardId.ToString()
                        ));
                    }
                    else
                    {
                        // Pattern: "archive cards matching 'pattern'"
                        var archiveCardsMatch = Regex.Match(
                            instruction,
                            @"^\s*archive cards matching ['""]([^'""]+)['""]\s*$",
                            RegexOptions.IgnoreCase);
                        if (archiveCardsMatch.Success)
                        {
                            var pattern = archiveCardsMatch.Groups[1].Value;

                            if (!boardId.HasValue)
                                return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, "Board ID is required for card operations");

                            // Find matching cards
                            var cards = await _unitOfWork.Cards.GetByBoardIdAsync(boardId.Value, cancellationToken);
                            var matchingCards = cards.Where(c =>
                                c.Title.Contains(pattern, StringComparison.OrdinalIgnoreCase)).ToList();

                            if (!matchingCards.Any())
                                return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"No cards matching '{pattern}' found");

                            foreach (var card in matchingCards)
                            {
                                var parameters = JsonSerializer.Serialize(new { cardId = card.Id });
                                operations.Add(new CreateProposalOperationDto(
                                    sequence++,
                                    "archive",
                                    "card",
                                    parameters,
                                    Guid.NewGuid().ToString(),
                                    TargetId: card.Id.ToString()
                                ));
                            }
                        }
                        else
                        {
                            // Pattern: "update card {id} title 'new title'" or "update card {id} description 'new desc'"
                            var updateCardMatch = Regex.Match(
                                instruction,
                                @"^\s*update card ([a-f0-9-]+)\s+(title|description) ['""]([^'""]+)['""]\s*$",
                                RegexOptions.IgnoreCase);
                            if (updateCardMatch.Success)
                            {
                                var cardIdStr = updateCardMatch.Groups[1].Value;
                                var field = updateCardMatch.Groups[2].Value.ToLowerInvariant();
                                var value = updateCardMatch.Groups[3].Value;

                                if (!Guid.TryParse(cardIdStr, out var cardId))
                                    return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, $"Invalid card ID: {cardIdStr}");

                                var parameters = field == "title"
                                    ? JsonSerializer.Serialize(new { cardId, title = value })
                                    : JsonSerializer.Serialize(new { cardId, description = value });

                                operations.Add(new CreateProposalOperationDto(
                                    sequence++,
                                    "update",
                                    "card",
                                    parameters,
                                    Guid.NewGuid().ToString(),
                                    TargetId: cardId.ToString()
                                ));
                            }
                            else
                            {
                                // Pattern: "rename board to 'name'"
                                var renameBoardMatch = Regex.Match(
                                    instruction,
                                    @"^\s*rename board to ['""]([^'""]+)['""]\s*$",
                                    RegexOptions.IgnoreCase);
                                if (renameBoardMatch.Success)
                                {
                                    if (!boardId.HasValue)
                                        return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, "Board ID is required for board operations");

                                    var name = renameBoardMatch.Groups[1].Value;
                                    var parameters = JsonSerializer.Serialize(new { boardId, name });
                                    operations.Add(new CreateProposalOperationDto(
                                        sequence++,
                                        "update",
                                        "board",
                                        parameters,
                                        Guid.NewGuid().ToString(),
                                        TargetId: boardId.Value.ToString()));
                                }
                                else
                                {
                                    // Pattern: "update board description 'value'" or "set board description to 'value'"
                                    var updateBoardDescriptionMatch = Regex.Match(
                                        instruction,
                                        @"^\s*(?:update|set) board description(?: to)? ['""]([^'""]+)['""]\s*$",
                                        RegexOptions.IgnoreCase);
                                    if (updateBoardDescriptionMatch.Success)
                                    {
                                        if (!boardId.HasValue)
                                            return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, "Board ID is required for board operations");

                                        var description = updateBoardDescriptionMatch.Groups[1].Value;
                                        var parameters = JsonSerializer.Serialize(new { boardId, description });
                                        operations.Add(new CreateProposalOperationDto(
                                            sequence++,
                                            "update",
                                            "board",
                                            parameters,
                                            Guid.NewGuid().ToString(),
                                            TargetId: boardId.Value.ToString()));
                                    }
                                    else
                                    {
                                        // Pattern: "archive board"
                                        var archiveBoardMatch = Regex.Match(
                                            instruction,
                                            @"^\s*archive board\s*$",
                                            RegexOptions.IgnoreCase);
                                        if (archiveBoardMatch.Success)
                                        {
                                            if (!boardId.HasValue)
                                                return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, "Board ID is required for board operations");

                                            var parameters = JsonSerializer.Serialize(new { boardId, isArchived = true });
                                            operations.Add(new CreateProposalOperationDto(
                                                sequence++,
                                                "update",
                                                "board",
                                                parameters,
                                                Guid.NewGuid().ToString(),
                                                TargetId: boardId.Value.ToString()));
                                        }
                                        else
                                        {
                                            // Pattern: "unarchive board"
                                            var unarchiveBoardMatch = Regex.Match(
                                                instruction,
                                                @"^\s*unarchive board\s*$",
                                                RegexOptions.IgnoreCase);
                                            if (unarchiveBoardMatch.Success)
                                            {
                                                if (!boardId.HasValue)
                                                    return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, "Board ID is required for board operations");

                                                var parameters = JsonSerializer.Serialize(new { boardId, isArchived = false });
                                                operations.Add(new CreateProposalOperationDto(
                                                    sequence++,
                                                    "update",
                                                    "board",
                                                    parameters,
                                                    Guid.NewGuid().ToString(),
                                                    TargetId: boardId.Value.ToString()));
                                            }
                                            else
                                            {
                                                // Pattern: "move column 'name' to position n"
                                                var moveColumnMatch = Regex.Match(
                                                    instruction,
                                                    @"^\s*move column ['""]([^'""]+)['""] to position (\d+)\s*$",
                                                    RegexOptions.IgnoreCase);
                                                if (moveColumnMatch.Success)
                                                {
                                                    if (!boardId.HasValue)
                                                        return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, "Board ID is required for column operations");

                                                    var columnName = moveColumnMatch.Groups[1].Value;
                                                    var positionStr = moveColumnMatch.Groups[2].Value;
                                                    if (!int.TryParse(positionStr, out var position) || position < 0)
                                                        return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, $"Invalid column position: {positionStr}");

                                                    var columns = (await _unitOfWork.Columns.GetByBoardIdAsync(boardId.Value, cancellationToken)).ToList();
                                                    if (!columns.Any())
                                                        return Result.Failure<ProposalDto>(ErrorCodes.NotFound, "No columns found in board");

                                                    var column = columns.FirstOrDefault(c => c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
                                                    if (column == null)
                                                        return Result.Failure<ProposalDto>(ErrorCodes.NotFound, $"Column '{columnName}' not found in board");

                                                    if (position >= columns.Count)
                                                        return Result.Failure<ProposalDto>(
                                                            ErrorCodes.ValidationError,
                                                            $"Invalid column position: {position}. Allowed range is 0 to {columns.Count - 1}");

                                                    var parameters = JsonSerializer.Serialize(new { columnId = column.Id, position });
                                                    operations.Add(new CreateProposalOperationDto(
                                                        sequence++,
                                                        "reorder",
                                                        "column",
                                                        parameters,
                                                        Guid.NewGuid().ToString(),
                                                        TargetId: column.Id.ToString()));
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (!operations.Any())
                return Result.Failure<ProposalDto>(ErrorCodes.ValidationError,
                    BuildParseHintMessage(instruction));

            // Classify risk
            var operationDtos = operations.Select(o => new ProposalOperationDto(
                Guid.NewGuid(),
                Guid.Empty,
                o.Sequence,
                o.ActionType,
                o.TargetType,
                o.TargetId,
                o.Parameters,
                o.IdempotencyKey,
                o.ExpectedVersion
            )).ToList();

            var riskLevel = _policyEngine.ClassifyRisk(operationDtos);

            // Create proposal
            var createDto = new CreateProposalDto(
                sourceType,
                userId,
                instruction.Length > 500 ? instruction.Substring(0, 497) + "..." : instruction,
                riskLevel,
                resolvedCorrelationId,
                boardId,
                normalizedSourceReferenceId,
                1440,
                operations
            );

            var result = await _proposalService.CreateProposalAsync(createDto, cancellationToken);
            if (!result.IsSuccess)
                return Result.Failure<ProposalDto>(result.ErrorCode, result.ErrorMessage);

            // Validate permissions
            var permissionResult = await _policyEngine.ValidatePermissionsAsync(userId, boardId, operationDtos, cancellationToken);
            if (!permissionResult.IsSuccess)
            {
                return Result.Failure<ProposalDto>(permissionResult.ErrorCode, permissionResult.ErrorMessage);
            }

            return Result.Success(result.Value);
        }
        catch (Exception ex)
        {
            return Result.Failure<ProposalDto>(ErrorCodes.UnexpectedError, $"Failed to parse instruction: {ex.Message}");
        }
    }

    internal static readonly string ParseHintMarker = "[PARSE_HINT]";

    internal static readonly (string Pattern, string Example, string[] Keywords)[] SupportedPatterns = new[]
    {
        ("create card \"title\"", "create card \"My new task\"", new[] { "create", "add", "new", "card", "task" }),
        ("create card \"title\" in column \"name\"", "create card \"Bug fix\" in column \"In Progress\"", new[] { "create", "add", "new", "card", "column", "in" }),
        ("move card {id} to column \"name\"", "move card abc-123 to column \"Done\"", new[] { "move", "card", "column", "to" }),
        ("archive card {id}", "archive card abc-123", new[] { "archive", "card", "remove", "delete" }),
        ("archive cards matching \"pattern\"", "archive cards matching \"old tasks\"", new[] { "archive", "cards", "matching", "bulk", "batch" }),
        ("update card {id} title \"value\"", "update card abc-123 title \"New title\"", new[] { "update", "edit", "change", "card", "title", "rename" }),
        ("update card {id} description \"value\"", "update card abc-123 description \"Updated details\"", new[] { "update", "edit", "change", "card", "description", "desc" }),
        ("rename board to \"name\"", "rename board to \"Sprint 5\"", new[] { "rename", "board", "name", "title" }),
        ("update board description \"value\"", "update board description \"Team workspace\"", new[] { "update", "board", "description", "desc" }),
        ("archive board", "archive board", new[] { "archive", "board" }),
        ("unarchive board", "unarchive board", new[] { "unarchive", "restore", "board" }),
        ("move column \"name\" to position {n}", "move column \"Done\" to position 0", new[] { "move", "column", "position", "reorder" }),
    };

    internal static string BuildParseHintMessage(string instruction)
    {
        var detectedIntent = DetectIntent(instruction);
        var bestMatch = FindClosestPattern(instruction, detectedIntent);

        var patterns = SupportedPatterns.Select(p => p.Pattern).ToArray();
        var hint = new ParseHintPayload(
            patterns,
            bestMatch.Example,
            bestMatch.Pattern,
            detectedIntent);

        var hintJson = JsonSerializer.Serialize(hint, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return $"Could not parse instruction into a proposal.{ParseHintMarker}{hintJson}";
    }

    internal static string? DetectIntent(string instruction)
    {
        var lower = instruction.Trim().ToLowerInvariant();
        var words = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Check more-specific intents before their substrings (e.g. "unarchive" before "archive",
        // "rename" before "new"). Use word-level matching to avoid substring false positives
        // like "sunset" matching "set" or "address" matching "add".
        bool hasWord(string word) => words.Any(w => w == word);

        if (lower.Contains("unarchive") || hasWord("restore"))
            return "unarchive";
        if (lower.Contains("rename") || hasWord("edit") || hasWord("change") || hasWord("update"))
            return "update";
        if (hasWord("reorder") || hasWord("position"))
            return "reorder";
        if (hasWord("create") || hasWord("add") || hasWord("new"))
            return "create";
        if (hasWord("move") || hasWord("drag") || hasWord("transfer"))
            return "move";
        if (hasWord("archive") || hasWord("remove") || hasWord("delete"))
            return "archive";

        return null;
    }

    internal static (string Pattern, string Example) FindClosestPattern(string instruction, string? detectedIntent)
    {
        var lower = instruction.Trim().ToLowerInvariant();
        var words = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var bestScore = -1;
        var bestPattern = SupportedPatterns[0];

        foreach (var entry in SupportedPatterns)
        {
            var score = 0;

            // Boost patterns whose keywords match whole words in the instruction
            foreach (var keyword in entry.Keywords)
            {
                if (words.Any(w => w == keyword))
                    score += 2;
            }

            // Extra boost if the detected intent matches the first keyword
            if (detectedIntent != null && entry.Keywords.Length > 0 &&
                entry.Keywords[0].Equals(detectedIntent, StringComparison.OrdinalIgnoreCase))
                score += 5;

            if (score > bestScore)
            {
                bestScore = score;
                bestPattern = entry;
            }
        }

        return (bestPattern.Pattern, bestPattern.Example);
    }

    internal record ParseHintPayload(
        string[] SupportedPatterns,
        string ExampleInstruction,
        string ClosestPattern,
        string? DetectedIntent);

    private static bool TryResolveCorrelationId(string? correlationId, out string resolvedCorrelationId, out string error)
    {
        if (correlationId == null)
        {
            resolvedCorrelationId = Guid.NewGuid().ToString();
            error = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            resolvedCorrelationId = string.Empty;
            error = "CorrelationId cannot be empty when provided";
            return false;
        }

        resolvedCorrelationId = correlationId.Trim();
        if (resolvedCorrelationId.Length > MaxProposalMetadataLength)
        {
            error = $"CorrelationId cannot exceed {MaxProposalMetadataLength} characters";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryResolveSourceReferenceId(string? sourceReferenceId, out string? normalizedSourceReferenceId, out string error)
    {
        if (sourceReferenceId == null)
        {
            normalizedSourceReferenceId = null;
            error = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(sourceReferenceId))
        {
            normalizedSourceReferenceId = null;
            error = "SourceReferenceId cannot be empty when provided";
            return false;
        }

        normalizedSourceReferenceId = sourceReferenceId.Trim();
        if (normalizedSourceReferenceId.Length > MaxProposalMetadataLength)
        {
            error = $"SourceReferenceId cannot exceed {MaxProposalMetadataLength} characters";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
