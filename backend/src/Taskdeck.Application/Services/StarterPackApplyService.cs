using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class StarterPackApplyService : IStarterPackApplyService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStarterPackManifestValidator _manifestValidator;
    private readonly StarterPackConflictDetector _conflictDetector;
    private readonly StarterPackIdempotencyChecker _idempotencyChecker;

    public StarterPackApplyService(
        IUnitOfWork unitOfWork,
        IStarterPackManifestValidator manifestValidator)
        : this(unitOfWork, manifestValidator, new StarterPackConflictDetector(), new StarterPackIdempotencyChecker())
    {
    }

    public StarterPackApplyService(
        IUnitOfWork unitOfWork,
        IStarterPackManifestValidator manifestValidator,
        StarterPackConflictDetector conflictDetector,
        StarterPackIdempotencyChecker idempotencyChecker)
    {
        _unitOfWork = unitOfWork;
        _manifestValidator = manifestValidator;
        _conflictDetector = conflictDetector;
        _idempotencyChecker = idempotencyChecker;
    }

    public async Task<Result<StarterPackApplyResultDto>> ApplyToBoardAsync(
        Guid boardId,
        ApplyStarterPackDto dto,
        CancellationToken cancellationToken = default)
    {
        if (boardId == Guid.Empty)
        {
            return Result.Failure<StarterPackApplyResultDto>(
                ErrorCodes.ValidationError,
                "Board ID cannot be empty.");
        }

        if (dto.Manifest == null)
        {
            return Result.Failure<StarterPackApplyResultDto>(
                ErrorCodes.ValidationError,
                "Manifest is required.");
        }

        var board = await _unitOfWork.Boards.GetByIdWithDetailsAsync(boardId, cancellationToken);
        if (board == null)
        {
            return Result.Failure<StarterPackApplyResultDto>(
                ErrorCodes.NotFound,
                $"Board with ID {boardId} not found.");
        }

        if (board.IsArchived)
        {
            return Result.Failure<StarterPackApplyResultDto>(
                ErrorCodes.InvalidOperation,
                "Cannot apply a starter pack to an archived board.");
        }

        var validationResult = _manifestValidator.Validate(dto.Manifest);
        if (!validationResult.IsValid)
        {
            return Result.Failure<StarterPackApplyResultDto>(
                ErrorCodes.ValidationError,
                BuildValidationSummary(validationResult.Errors));
        }

        var manifest = validationResult.Manifest!;

        var conflictReport = _conflictDetector.DetectConflicts(board, manifest);
        var plannedSeedCards = _idempotencyChecker.Check(board, manifest, conflictReport);

        var preview = new StarterPackApplyResultDto(
            board.Id,
            manifest.PackId,
            dto.DryRun,
            false,
            conflictReport.Actions,
            conflictReport.Conflicts);

        if (dto.DryRun || preview.HasBlockingConflicts)
        {
            return Result.Success(preview);
        }

        return await ApplyPlanAsync(
            board,
            conflictReport,
            plannedSeedCards,
            preview,
            cancellationToken);
    }

    private async Task<Result<StarterPackApplyResultDto>> ApplyPlanAsync(
        Board board,
        StarterPackConflictReport conflictReport,
        List<StarterPackIdempotencyChecker.PlannedSeedCard> plannedSeedCards,
        StarterPackApplyResultDto preview,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var labelsByName = new Dictionary<string, Label>(StringComparer.OrdinalIgnoreCase);
            foreach (var label in board.Labels)
            {
                labelsByName[label.Name] = label;
            }

            foreach (var plannedLabel in conflictReport.PlannedLabels)
            {
                var label = new Label(board.Id, plannedLabel.Name, plannedLabel.Color);
                await _unitOfWork.Labels.AddAsync(label, cancellationToken);
                labelsByName[label.Name] = label;
            }

            var columnsByName = new Dictionary<string, Column>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in board.Columns)
            {
                columnsByName[column.Name] = column;
            }

            foreach (var plannedColumn in conflictReport.PlannedColumns)
            {
                var column = new Column(board.Id, plannedColumn.Name, plannedColumn.Position, plannedColumn.WipLimit);
                await _unitOfWork.Columns.AddAsync(column, cancellationToken);
                columnsByName[column.Name] = column;
            }

            var nextPositionByColumnId = board.Columns.ToDictionary(
                column => column.Id,
                column => column.Cards.Any() ? column.Cards.Max(card => card.Position) + 1 : 0);

            foreach (var plannedColumn in conflictReport.PlannedColumns)
            {
                if (columnsByName.TryGetValue(plannedColumn.Name, out var column))
                {
                    nextPositionByColumnId[column.Id] = 0;
                }
            }

            foreach (var plannedSeedCard in plannedSeedCards)
            {
                if (!columnsByName.TryGetValue(plannedSeedCard.ColumnName, out var column))
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return Result.Failure<StarterPackApplyResultDto>(
                        ErrorCodes.UnexpectedError,
                        $"Resolved column '{plannedSeedCard.ColumnName}' was not available during apply.");
                }

                if (!nextPositionByColumnId.TryGetValue(column.Id, out var nextPosition))
                {
                    nextPosition = 0;
                }

                var card = new Card(
                    board.Id,
                    column.Id,
                    plannedSeedCard.SeedCard.Title,
                    plannedSeedCard.SeedCard.Description,
                    null,
                    nextPosition);

                foreach (var labelName in plannedSeedCard.LabelNames)
                {
                    if (!labelsByName.TryGetValue(labelName, out var label))
                    {
                        await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                        return Result.Failure<StarterPackApplyResultDto>(
                            ErrorCodes.UnexpectedError,
                            $"Resolved label '{labelName}' was not available during apply.");
                    }

                    card.AddLabel(new CardLabel(card.Id, label.Id));
                }

                await _unitOfWork.Cards.AddAsync(card, cancellationToken);
                nextPositionByColumnId[column.Id] = nextPosition + 1;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            var appliedResult = preview with { Applied = true };
            return Result.Success(appliedResult);
        }
        catch (DomainException ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<StarterPackApplyResultDto>(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<StarterPackApplyResultDto>(
                ErrorCodes.UnexpectedError,
                $"Starter pack apply failed: {ex.Message}");
        }
    }

    private static string BuildValidationSummary(IReadOnlyList<StarterPackManifestValidationError> errors)
    {
        if (errors.Count == 0)
        {
            return "Manifest validation failed.";
        }

        var topErrors = errors
            .Take(5)
            .Select(error => $"{error.Path}: {error.Message}");
        var suffix = errors.Count > 5
            ? $" (+{errors.Count - 5} more)"
            : string.Empty;

        return $"Manifest validation failed: {string.Join("; ", topErrors)}{suffix}";
    }
}
