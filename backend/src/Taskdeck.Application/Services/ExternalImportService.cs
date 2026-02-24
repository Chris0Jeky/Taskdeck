using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class ExternalImportService : IExternalImportService
{
    private const string ImportMetadataPrefix = "[taskdeck-import-meta] ";
    private static readonly JsonSerializerOptions MetadataDeserializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly IReadOnlyDictionary<string, IExternalImportAdapter> _adaptersByProvider;

    public ExternalImportService(
        IUnitOfWork unitOfWork,
        IEnumerable<IExternalImportAdapter> adapters)
    {
        _unitOfWork = unitOfWork;
        _adaptersByProvider = adapters
            .GroupBy(adapter => adapter.Provider, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Result<ExternalImportResultDto>> ImportToBoardAsync(
        Guid boardId,
        ExternalImportRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (boardId == Guid.Empty)
        {
            return Result.Failure<ExternalImportResultDto>(
                ErrorCodes.ValidationError,
                "Board ID cannot be empty.");
        }

        if (request == null)
        {
            return Result.Failure<ExternalImportResultDto>(
                ErrorCodes.ValidationError,
                "Import request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            return Result.Failure<ExternalImportResultDto>(
                ErrorCodes.ValidationError,
                "Import provider is required.");
        }

        var normalizedProvider = request.Provider.Trim();
        if (!_adaptersByProvider.TryGetValue(normalizedProvider, out var adapter))
        {
            return Result.Failure<ExternalImportResultDto>(
                ErrorCodes.ValidationError,
                $"Unsupported import provider '{request.Provider}'.");
        }

        if (string.IsNullOrWhiteSpace(request.TargetColumnName))
        {
            return Result.Failure<ExternalImportResultDto>(
                ErrorCodes.ValidationError,
                "Target column name is required.");
        }

        var board = await _unitOfWork.Boards.GetByIdWithDetailsAsync(boardId, cancellationToken);
        if (board == null)
        {
            return Result.Failure<ExternalImportResultDto>(
                ErrorCodes.NotFound,
                $"Board with ID {boardId} not found.");
        }

        if (board.IsArchived)
        {
            return Result.Failure<ExternalImportResultDto>(
                ErrorCodes.InvalidOperation,
                "Cannot import into an archived board.");
        }

        var targetColumn = board.Columns.FirstOrDefault(column =>
            string.Equals(column.Name, request.TargetColumnName, StringComparison.OrdinalIgnoreCase));
        if (targetColumn == null)
        {
            return Result.Failure<ExternalImportResultDto>(
                ErrorCodes.ValidationError,
                $"Target column '{request.TargetColumnName}' was not found on board '{board.Name}'.");
        }

        var parseResult = adapter.Parse(request with { Provider = normalizedProvider });
        if (!parseResult.IsSuccess)
        {
            return Result.Failure<ExternalImportResultDto>(parseResult.ErrorCode, parseResult.ErrorMessage);
        }

        var parsed = parseResult.Value;
        var conflicts = new List<ExternalImportConflictDto>(parsed.Conflicts);

        var existingByDedupeKey = new Dictionary<string, Card>(StringComparer.OrdinalIgnoreCase);
        var duplicateExistingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in board.Columns.SelectMany(column => column.Cards))
        {
            if (!TryReadImportMetadata(card.Description, out var metadata) ||
                string.IsNullOrWhiteSpace(metadata.DedupeKey))
            {
                continue;
            }

            if (!existingByDedupeKey.TryAdd(metadata.DedupeKey, card))
            {
                duplicateExistingKeys.Add(metadata.DedupeKey);
            }
        }

        foreach (var duplicateKey in duplicateExistingKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            conflicts.Add(new ExternalImportConflictDto(
                "ExistingDuplicateDedupeKey",
                "$.board.cards",
                $"Board already contains multiple cards with dedupe key '{duplicateKey}'. Resolve duplicates before applying import."));
        }

        var plannedUpserts = new List<PlannedUpsert>();
        var rowsCreated = 0;
        var rowsUpdated = 0;
        var rowsSkipped = 0;

        foreach (var candidate in parsed.Candidates)
        {
            if (duplicateExistingKeys.Contains(candidate.DedupeKey))
            {
                conflicts.Add(new ExternalImportConflictDto(
                    "AmbiguousExistingMatch",
                    $"$.rows[{candidate.SourceRowNumber}]",
                    $"Cannot resolve target card for dedupe key '{candidate.DedupeKey}' because multiple existing cards match."));
                continue;
            }

            if (existingByDedupeKey.TryGetValue(candidate.DedupeKey, out var existingCard))
            {
                var requiresTitleUpdate = !string.Equals(existingCard.Title, candidate.Title, StringComparison.Ordinal);
                var requiresDescriptionUpdate = !string.Equals(existingCard.Description, candidate.Description, StringComparison.Ordinal);
                var requiresMove = existingCard.ColumnId != targetColumn.Id;

                if (!requiresTitleUpdate && !requiresDescriptionUpdate && !requiresMove)
                {
                    rowsSkipped++;
                    continue;
                }

                plannedUpserts.Add(new PlannedUpsert(existingCard, candidate));
                rowsUpdated++;
                continue;
            }

            plannedUpserts.Add(new PlannedUpsert(null, candidate));
            rowsCreated++;
        }

        var preview = new ExternalImportResultDto(
            board.Id,
            parsed.Provider,
            parsed.Profile,
            targetColumn.Name,
            request.DryRun,
            Applied: false,
            RowsReceived: parsed.RowsReceived,
            RowsParsed: parsed.RowsParsed,
            RowsCreated: rowsCreated,
            RowsUpdated: rowsUpdated,
            RowsSkipped: rowsSkipped,
            Conflicts: conflicts);

        if (request.DryRun || preview.HasConflicts)
        {
            return Result.Success(preview);
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var nextTargetPosition = targetColumn.Cards.Any()
                ? targetColumn.Cards.Max(card => card.Position) + 1
                : 0;

            foreach (var upsert in plannedUpserts)
            {
                if (upsert.ExistingCard is null)
                {
                    var newCard = new Card(
                        board.Id,
                        targetColumn.Id,
                        upsert.Candidate.Title,
                        upsert.Candidate.Description,
                        dueDate: null,
                        position: nextTargetPosition++);
                    await _unitOfWork.Cards.AddAsync(newCard, cancellationToken);
                    continue;
                }

                var card = upsert.ExistingCard;
                card.Update(
                    title: upsert.Candidate.Title,
                    description: upsert.Candidate.Description);

                if (card.ColumnId != targetColumn.Id)
                {
                    card.MoveToColumn(targetColumn.Id, nextTargetPosition++);
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return Result.Success(preview with { Applied = true });
        }
        catch (DomainException ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<ExternalImportResultDto>(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            return Result.Failure<ExternalImportResultDto>(
                ErrorCodes.UnexpectedError,
                $"External import failed: {ex.Message}");
        }
    }

    private static bool TryReadImportMetadata(string description, out ExistingCardImportMetadata metadata)
    {
        metadata = new ExistingCardImportMetadata(string.Empty, string.Empty, string.Empty);

        if (string.IsNullOrWhiteSpace(description) ||
            !description.StartsWith(ImportMetadataPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var lineBreakIndex = description.IndexOfAny(['\r', '\n']);
        var metadataLine = lineBreakIndex >= 0
            ? description[..lineBreakIndex]
            : description;
        var jsonPayload = metadataLine[ImportMetadataPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(jsonPayload))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ExistingCardImportMetadata>(jsonPayload, MetadataDeserializerOptions);
            if (parsed is null)
            {
                return false;
            }

            metadata = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record PlannedUpsert(Card? ExistingCard, ExternalImportCandidate Candidate);

    private sealed record ExistingCardImportMetadata(string Provider, string Profile, string DedupeKey);
}
