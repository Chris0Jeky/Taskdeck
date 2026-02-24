using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class ExternalImportService : IExternalImportService
{
    private static readonly JsonSerializerOptions MetadataDeserializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly ImportMatchKeyComparer MatchKeyComparer = new();

    private readonly IUnitOfWork _unitOfWork;
    private readonly IReadOnlyDictionary<string, IExternalImportAdapter> _adaptersByProvider;

    public ExternalImportService(
        IUnitOfWork unitOfWork,
        IEnumerable<IExternalImportAdapter> adapters)
    {
        _unitOfWork = unitOfWork;
        var normalizedAdapters = adapters
            .Select(adapter => (Adapter: adapter, Provider: NormalizeProviderKey(adapter.Provider)))
            .ToList();

        var adapterGroups = normalizedAdapters
            .GroupBy(entry => entry.Provider, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var duplicateProviders = adapterGroups
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(provider => provider, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (duplicateProviders.Length > 0)
        {
            throw new InvalidOperationException(
                "Multiple external import adapters are registered for provider(s): " +
                string.Join(", ", duplicateProviders) +
                ". Each provider must have exactly one adapter implementation.");
        }

        _adaptersByProvider = adapterGroups
            .ToDictionary(group => group.Key, group => group.Single().Adapter, StringComparer.OrdinalIgnoreCase);
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
        var parsedProvider = parsed.Provider.Trim();
        var parsedProfile = parsed.Profile.Trim();
        var conflicts = new List<ExternalImportConflictDto>(parsed.Conflicts);

        var existingCardsByMatchKey = new Dictionary<ImportMatchKey, List<Card>>(MatchKeyComparer);
        foreach (var card in board.Columns.SelectMany(column => column.Cards))
        {
            if (!TryReadImportMetadata(card.Description, out var metadata) ||
                string.IsNullOrWhiteSpace(metadata.Provider) ||
                string.IsNullOrWhiteSpace(metadata.Profile) ||
                string.IsNullOrWhiteSpace(metadata.DedupeKey))
            {
                continue;
            }

            var metadataProvider = metadata.Provider.Trim();
            var metadataProfile = metadata.Profile.Trim();
            if (!string.Equals(metadataProvider, parsedProvider, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(metadataProfile, parsedProfile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var matchKey = new ImportMatchKey(metadataProvider, metadataProfile, metadata.DedupeKey.Trim());
            if (!existingCardsByMatchKey.TryGetValue(matchKey, out var matchingCards))
            {
                matchingCards = [];
                existingCardsByMatchKey[matchKey] = matchingCards;
            }

            matchingCards.Add(card);
        }

        var existingByMatchKey = existingCardsByMatchKey
            .Where(entry => entry.Value.Count == 1)
            .ToDictionary(entry => entry.Key, entry => entry.Value[0], MatchKeyComparer);
        var duplicateExistingKeys = existingCardsByMatchKey
            .Where(entry => entry.Value.Count > 1)
            .Select(entry => entry.Key)
            .ToHashSet(MatchKeyComparer);

        foreach (var duplicateKey in duplicateExistingKeys
                     .OrderBy(key => key.Provider, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(key => key.Profile, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(key => key.DedupeKey, StringComparer.OrdinalIgnoreCase))
        {
            var existingCards = existingCardsByMatchKey[duplicateKey];
            conflicts.Add(new ExternalImportConflictDto(
                "ExistingDuplicateDedupeKey",
                "$.board.cards",
                $"Board already contains multiple cards with dedupe key '{duplicateKey.DedupeKey}' for provider '{duplicateKey.Provider}' and profile '{duplicateKey.Profile}'. Resolve duplicates before applying import.",
                ExistingValue: BuildCardReference(existingCards),
                IncomingValue: duplicateKey.DedupeKey));
        }

        var plannedUpserts = new List<PlannedUpsert>();
        var rowsCreated = 0;
        var rowsUpdated = 0;
        var rowsSkipped = 0;

        foreach (var candidate in parsed.Candidates)
        {
            var candidateMatchKey = new ImportMatchKey(parsedProvider, parsedProfile, candidate.DedupeKey.Trim());

            if (duplicateExistingKeys.Contains(candidateMatchKey))
            {
                var existingCards = existingCardsByMatchKey[candidateMatchKey];
                conflicts.Add(new ExternalImportConflictDto(
                    "AmbiguousExistingMatch",
                    $"$.rows[{candidate.SourceRowNumber}]",
                    $"Cannot resolve target card for dedupe key '{candidate.DedupeKey}' because multiple existing cards match for provider '{parsedProvider}' and profile '{parsedProfile}'.",
                    ExistingValue: BuildCardReference(existingCards),
                    IncomingValue: candidate.DedupeKey));
                continue;
            }

            if (existingByMatchKey.TryGetValue(candidateMatchKey, out var existingCard))
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
            parsedProvider,
            parsedProfile,
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

        var cardsMovingIntoTarget = plannedUpserts.Count(upsert =>
            upsert.ExistingCard is null || upsert.ExistingCard.ColumnId != targetColumn.Id);
        if (WouldExceedTargetColumnWipLimit(targetColumn, cardsMovingIntoTarget))
        {
            return Result.Failure<ExternalImportResultDto>(
                ErrorCodes.WipLimitExceeded,
                $"Cannot import cards, target column '{targetColumn.Name}' has reached its WIP limit of {targetColumn.WipLimit}.");
        }

        var transactionStarted = false;

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            transactionStarted = true;

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
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            }

            return Result.Failure<ExternalImportResultDto>(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            }

            return Result.Failure<ExternalImportResultDto>(
                ErrorCodes.UnexpectedError,
                $"External import failed: {ex.Message}");
        }
    }

    private static bool TryReadImportMetadata(string description, out ExistingCardImportMetadata metadata)
    {
        metadata = new ExistingCardImportMetadata(string.Empty, string.Empty, string.Empty);

        if (string.IsNullOrWhiteSpace(description) ||
            !description.StartsWith(ExternalImportMetadata.CardDescriptionPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var lineBreakIndex = description.IndexOfAny(['\r', '\n']);
        var metadataLine = lineBreakIndex >= 0
            ? description[..lineBreakIndex]
            : description;
        var jsonPayload = metadataLine[ExternalImportMetadata.CardDescriptionPrefix.Length..].Trim();
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

    private static string NormalizeProviderKey(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new InvalidOperationException(
                "External import adapter registration contains an empty provider key. " +
                "Each adapter must declare a non-empty provider.");
        }

        return provider.Trim();
    }

    private static bool WouldExceedTargetColumnWipLimit(Column targetColumn, int cardsMovingIntoTarget)
    {
        if (!targetColumn.WipLimit.HasValue || cardsMovingIntoTarget <= 0)
        {
            return false;
        }

        var projectedCardCount = targetColumn.Cards.Count + cardsMovingIntoTarget;
        return projectedCardCount > targetColumn.WipLimit.Value;
    }

    private sealed record PlannedUpsert(Card? ExistingCard, ExternalImportCandidate Candidate);

    private sealed record ExistingCardImportMetadata(string Provider, string Profile, string DedupeKey);
    private sealed record ImportMatchKey(string Provider, string Profile, string DedupeKey);

    private static string BuildCardReference(IEnumerable<Card> cards)
    {
        return string.Join(", ", cards.Select(card => $"{card.Id}:{card.Title}"));
    }

    private sealed class ImportMatchKeyComparer : IEqualityComparer<ImportMatchKey>
    {
        public bool Equals(ImportMatchKey? x, ImportMatchKey? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return string.Equals(x.Provider, y.Provider, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(x.Profile, y.Profile, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(x.DedupeKey, y.DedupeKey, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(ImportMatchKey obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Provider),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Profile),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.DedupeKey));
        }
    }
}
