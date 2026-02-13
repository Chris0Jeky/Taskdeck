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

    public async Task<Result<ProposalDto>> ParseInstructionAsync(string instruction, Guid userId, Guid? boardId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instruction))
            return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, "Instruction cannot be empty");

        if (userId == Guid.Empty)
            return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, "UserId cannot be empty");

        try
        {
            var operations = new List<CreateProposalOperationDto>();
            var instructionLower = instruction.ToLowerInvariant();
            var sequence = 0;

            // Pattern: "create card 'title' in column 'column name'" or "create card 'title'"
            var createCardMatch = Regex.Match(instruction, @"create card ['""]([^'""]+)['""](?:\s+in column ['""]([^'""]+)['""])?(?:\s+with description ['""]([^'""]+)['""])?", RegexOptions.IgnoreCase);
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

            // Pattern: "move card {id} to column 'column name'"
            var moveCardMatch = Regex.Match(instruction, @"move card ([a-f0-9-]+) to column ['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
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

            // Pattern: "archive card {id}" or "archive cards matching 'pattern'"
            var archiveCardMatch = Regex.Match(instruction, @"archive card ([a-f0-9-]+)", RegexOptions.IgnoreCase);
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

            // Pattern: "archive cards matching 'pattern'"
            var archiveCardsMatch = Regex.Match(instruction, @"archive cards matching ['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
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

            // Pattern: "update card {id} title 'new title'" or "update card {id} description 'new desc'"
            var updateCardMatch = Regex.Match(instruction, @"update card ([a-f0-9-]+)\s+(title|description) ['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
            if (updateCardMatch.Success)
            {
                var cardIdStr = updateCardMatch.Groups[1].Value;
                var field = updateCardMatch.Groups[2].Value.ToLower();
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

            if (!operations.Any())
                return Result.Failure<ProposalDto>(ErrorCodes.ValidationError, 
                    "Could not parse instruction. Supported patterns: 'create card \"title\"', 'move card {id} to column \"name\"', 'archive card {id}', 'archive cards matching \"pattern\"', 'update card {id} title/description \"value\"'");

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
                ProposalSourceType.Manual,
                userId,
                instruction.Length > 500 ? instruction.Substring(0, 497) + "..." : instruction,
                riskLevel,
                Guid.NewGuid().ToString(),
                boardId,
                null,
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
}
