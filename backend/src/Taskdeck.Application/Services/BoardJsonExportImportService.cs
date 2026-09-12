using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class BoardJsonExportImportService : IBoardJsonExportImportService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly DevelopmentSandboxSettings _sandboxSettings;
    private readonly IThinkingDeckRepository? _thinkingDecks;
    private readonly IBoardDependencyRepository? _dependencies;
    private readonly ICardAssignmentStore? _assignments;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public BoardJsonExportImportService(
        IUnitOfWork unitOfWork,
        DevelopmentSandboxSettings? sandboxSettings = null,
        IThinkingDeckRepository? thinkingDecks = null,
        IBoardDependencyRepository? dependencies = null,
        ICardAssignmentStore? assignments = null)
    {
        _unitOfWork = unitOfWork;
        _sandboxSettings = sandboxSettings ?? new DevelopmentSandboxSettings();
        _thinkingDecks = thinkingDecks;
        _dependencies = dependencies;
        _assignments = assignments;
    }

    public async Task<Result<ExportBoardDto>> ExportBoardAsync(Guid boardId, Guid userId)
    {
        try
        {
            var requestingUser = await _unitOfWork.Users.GetByIdAsync(userId);
            if (requestingUser == null)
                return Result.Failure<ExportBoardDto>(ErrorCodes.NotFound, $"User with ID {userId} not found");

            var board = await _unitOfWork.Boards.GetByIdWithDetailsAsync(boardId);
            if (board == null)
                return Result.Failure<ExportBoardDto>(ErrorCodes.NotFound, $"Board with ID {boardId} not found");

            var canRead = await CanUserReadBoardAsync(board, userId);
            if (!canRead)
                return Result.Failure<ExportBoardDto>(ErrorCodes.Forbidden, "You do not have access to export this board");

            var accesses = await _unitOfWork.BoardAccesses.GetByBoardIdAsync(boardId);

            var boardDto = MapToBoardDto(board);

            var columns = board.Columns
                .OrderBy(c => c.Position)
                .Select(c => new ColumnDto(
                    c.Id,
                    c.BoardId,
                    c.Name,
                    c.Position,
                    c.WipLimit,
                    c.Cards.Count,
                    c.CreatedAt,
                    c.UpdatedAt))
                .ToList();

            var cards = board.Columns
                .OrderBy(c => c.Position)
                .SelectMany(c => c.Cards.OrderBy(card => card.Position))
                .Select(MapToCardDto)
                .ToList();

            var labels = board.Labels
                .Select(l => new LabelDto(
                    l.Id,
                    l.BoardId,
                    l.Name,
                    l.ColorHex,
                    l.CreatedAt,
                    l.UpdatedAt))
                .ToList();

            var accessDtos = accesses
                .Select(a => new BoardAccessDto(
                    a.Id,
                    a.BoardId,
                    a.UserId,
                    a.Role,
                    a.GrantedBy,
                    a.GrantedAt))
                .ToList();

            var exportedIds = cards.Select(card => card.Id).ToHashSet();
            var thinkingDecks = _thinkingDecks is null ? null :
                (await _thinkingDecks.GetByCardIdsAsync(cards.Select(card => card.Id).ToArray(), default))
                    .Select(deck => new ExportThinkingDeckDto(deck.CardId, new ThinkingMaterialDto(deck.SchemaVersion, deck.ReadLayers().Select(layer => layer with
                    {
                        Items = layer.Items.Select(item => item.LinkedCardId.HasValue && !exportedIds.Contains(item.LinkedCardId.Value)
                            ? item with { LinkedCardId = null } : item).ToArray()
                    }).ToArray())))
                    .ToList();

            var exportDto = new ExportBoardDto(
                boardDto,
                columns,
                cards,
                labels,
                accessDtos,
                DateTimeOffset.UtcNow,
                requestingUser.Username,
                thinkingDecks,
                _dependencies is null ? null : (await _dependencies.GetAsync(boardId, CancellationToken.None))?
                    .ReadEdges().Where(edge => exportedIds.Contains(edge.CardId) && exportedIds.Contains(edge.DependsOnCardId)).ToArray());

            return Result.Success(exportDto);
        }
        catch (DomainException ex)
        {
            return Result.Failure<ExportBoardDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<string>> ExportBoardToJsonAsync(Guid boardId, Guid userId)
    {
        var exportResult = await ExportBoardAsync(boardId, userId);
        if (!exportResult.IsSuccess)
            return Result.Failure<string>(exportResult.ErrorCode, exportResult.ErrorMessage);

        var json = JsonSerializer.Serialize(ToPortablePayload(exportResult.Value), JsonOptions);
        return Result.Success(json);
    }

    public Task<Result<ImportResultDto>> ImportBoardAsync(ImportBoardDto dto, Guid userId)
        => ImportBoardCoreAsync(dto, userId, preview: false);

    public async Task<Result<BoardImportPreviewDto>> PreviewBoardAsync(string json, Guid userId)
    {
        try
        {
            var dto = TryDeserializeImportDto(json);
            if (dto is null) return Result.Failure<BoardImportPreviewDto>(ErrorCodes.ValidationError, "Invalid or unsupported board JSON.");
            var validation = await ImportBoardCoreAsync(dto, userId, preview: true);
            if (!validation.IsSuccess) return Result.Failure<BoardImportPreviewDto>(validation.ErrorCode, validation.ErrorMessage);
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            var cards = (dto.Cards ?? []).ToArray();
            var sources = cards.SelectMany((c, index) => (c.SourceAssignees ?? []).Select(a => new { Assignee = a, CardIndex = index }))
                .GroupBy(x => x.Assignee.SourceKey, StringComparer.Ordinal)
                .Select(g => new ImportAssigneePreviewDto(g.Key, g.First().Assignee.DisplayName, g.Select(x => x.CardIndex).Distinct().Count()))
                .OrderBy(x => x.DisplayName).ToArray();
            return Result.Success(new BoardImportPreviewDto(dto, cards.Length, (dto.Columns ?? []).Count(), sources,
                new BoardParticipantDto(userId, user!.Username)));
        }
        catch (Exception ex) when (ex is JsonException or DomainException or ArgumentException)
        {
            return Result.Failure<BoardImportPreviewDto>(ErrorCodes.ValidationError, "Invalid board import payload.");
        }
    }

    private async Task<Result<ImportResultDto>> ImportBoardCoreAsync(ImportBoardDto dto, Guid userId, bool preview)
    {
        try
        {
            await _unitOfWork.BeginTransactionAsync();
            if (_assignments is not null)
                await _assignments.RefreshAuthorityAsync(Guid.Empty, userId, default);
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user is null)
                throw new DomainException(ErrorCodes.NotFound, "Importing user not found.");
            if (user is not { IsActive: true })
                throw new DomainException(ErrorCodes.Forbidden, "An active account is required to import a board.");

            var labels = dto.Labels ?? Enumerable.Empty<ImportLabelDto>();
            var columns = dto.Columns ?? Enumerable.Empty<ImportColumnDto>();
            var cards = (dto.Cards ?? Enumerable.Empty<ImportCardDto>()).ToList();
            // Validate the entire payload before any board or card is added, including preview.
            foreach (var card in cards)
                if (card.EstimatedEffortMinutes is < 0 or > Card.MaxEstimatedEffortMinutes)
                    throw new DomainException(ErrorCodes.ValidationError,
                        $"Estimated effort for card '{card.Title}' must be between 0 and {Card.MaxEstimatedEffortMinutes} minutes, or unknown.");
            var sourceNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var source in cards.SelectMany(c => c.SourceAssignees ?? []))
            {
                if (source is null || string.IsNullOrWhiteSpace(source.SourceKey) || source.SourceKey.Length > 200 ||
                    string.IsNullOrWhiteSpace(source.DisplayName) || source.DisplayName.Length > 200)
                    throw new DomainException(ErrorCodes.ValidationError, "Every source assignee needs a bounded key and display name.");
                // Preview groups by source key: never hide a conflicting label behind its first occurrence.
                if (!sourceNames.TryAdd(source.SourceKey, source.DisplayName) &&
                    !string.Equals(sourceNames[source.SourceKey], source.DisplayName, StringComparison.Ordinal))
                    throw new DomainException(ErrorCodes.ValidationError,
                        "Each source assignee key must have one consistent display name across the import.");
            }
            if (dto.AssigneeMappings is not null && dto.AssigneeMappings.Any(m => !sourceNames.ContainsKey(m.Key) ||
                    m.Value.HasValue && m.Value != userId))
                throw new DomainException(ErrorCodes.ValidationError, "Mappings may target only Me or explicit unassigned, and must reference a source assignee.");
            if (!preview && sourceNames.Keys.Any(key => dto.AssigneeMappings is null || !dto.AssigneeMappings.ContainsKey(key)))
                throw new DomainException(ErrorCodes.ValidationError, "Explicitly map every source assignee before importing.");
            var cardIds = new Dictionary<Guid, Guid>();
            foreach (var source in cards.Where(card => card.SourceId.HasValue))
                if (source.SourceId == Guid.Empty || !cardIds.TryAdd(source.SourceId!.Value, Guid.NewGuid()))
                    throw new DomainException(ErrorCodes.ValidationError, "Invalid or duplicate source card ID.");

            // Parent archive state belongs to the full graph contract: the create, update and proposal
            // lanes all refuse an archived parent, and ordinary archival detaches every direct child, so
            // only a hand-crafted payload can name one. Reject before anything is constructed, next to the
            // other whole-payload checks, so a rejected graph can never leave a partially created board.
            // Archived children whose parent stays active remain valid and still import.
            var archivedSourceIds = cards
                .Where(card => card.IsArchived && card.SourceId is Guid archivedId && archivedId != Guid.Empty)
                .Select(card => card.SourceId!.Value)
                .ToHashSet();
            foreach (var card in cards)
                if (card.ParentCardId is Guid parentSourceId && archivedSourceIds.Contains(parentSourceId))
                    throw new DomainException(
                        ErrorCodes.ValidationError,
                        $"Card '{card.Title}' references an archived parent. Restore the parent card before assigning it.");

            var board = new Board(dto.Name, dto.Description, userId);
            await _unitOfWork.Boards.AddAsync(board);

            // Create labels and track by name
            var labelsByName = new Dictionary<string, Label>(StringComparer.OrdinalIgnoreCase);
            foreach (var importLabel in labels)
            {
                if (labelsByName.ContainsKey(importLabel.Name))
                    throw new DomainException(ErrorCodes.ValidationError, $"Duplicate label name '{importLabel.Name}' in import payload");

                var label = new Label(board.Id, importLabel.Name, importLabel.Color);
                await _unitOfWork.Labels.AddAsync(label);
                labelsByName[importLabel.Name] = label;
            }

            // Create columns and track by name
            var columnsByName = new Dictionary<string, Column>(StringComparer.OrdinalIgnoreCase);
            foreach (var importColumn in columns.OrderBy(c => c.Position))
            {
                if (columnsByName.ContainsKey(importColumn.Name))
                    throw new DomainException(ErrorCodes.ValidationError, $"Duplicate column name '{importColumn.Name}' in import payload");

                var column = new Column(board.Id, importColumn.Name, importColumn.Position, importColumn.WipLimit);
                await _unitOfWork.Columns.AddAsync(column);
                columnsByName[importColumn.Name] = column;
            }

            // Create cards with label associations
            var importedCards = new List<Card>();
            var cardsImported = 0;
            foreach (var importCard in cards.OrderBy(c => c.Position))
            {
                if (!columnsByName.TryGetValue(importCard.ColumnName, out var column))
                    throw new DomainException(ErrorCodes.ValidationError, $"Column '{importCard.ColumnName}' referenced by card '{importCard.Title}' was not found");

                var card = new Card(importCard.SourceId.HasValue ? cardIds[importCard.SourceId.Value] : Guid.NewGuid(),
                    board.Id, column.Id, importCard.Title, importCard.Description, importCard.DueDate, importCard.Position);
                card.SetWorkItemType(Card.ParseWorkItemType(importCard.WorkItemType));
                card.SetEstimatedEffortMinutes(importCard.EstimatedEffortMinutes);
                if (importCard.ParentCardId is Guid sourceParent)
                {
                    if (!cardIds.TryGetValue(sourceParent, out var newParent))
                        throw new DomainException(ErrorCodes.ValidationError, "Parent must reference a card inside this import.");
                    card.SetParent(newParent);
                }
                importedCards.Add(card);
                var uniqueCardLabelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var labelName in importCard.Labels ?? Enumerable.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(labelName))
                    {
                        throw new DomainException(
                            ErrorCodes.ValidationError,
                            $"Card '{importCard.Title}' contains an empty label reference");
                    }

                    if (!uniqueCardLabelNames.Add(labelName))
                        continue;

                    if (!labelsByName.TryGetValue(labelName, out var label))
                    {
                        throw new DomainException(
                            ErrorCodes.ValidationError,
                            $"Label '{labelName}' referenced by card '{importCard.Title}' was not found");
                    }

                    var cardLabel = new CardLabel(card.Id, label.Id);
                    card.AddLabel(cardLabel);
                }

                var mappedUsers = (importCard.SourceAssignees ?? []).Select(a =>
                    dto.AssigneeMappings?.GetValueOrDefault(a.SourceKey)).Where(id => id.HasValue).Select(id => id!.Value);
                card.ReplaceAssignments(mappedUsers, userId);
                if (card.Assignments.Count > 0)
                    await _unitOfWork.AuditLogs.AddAsync(new AuditLog("card", card.Id, Taskdeck.Domain.Enums.AuditAction.Created, userId,
                        JsonSerializer.Serialize(new { reason = "assignment-import-mapping", assigneeUserIds = card.Assignments.Select(a => a.UserId).ToArray() })));
                if (importCard.IsArchived) card.Archive();
                await _unitOfWork.Cards.AddAsync(card);
                if (importCard.Thinking is not null)
                {
                    if (_thinkingDecks is null)
                        throw new DomainException(ErrorCodes.ValidationError, "This host cannot import thinking material.");
                    if (importCard.Thinking.SchemaVersion is not (1 or 2))
                        throw new DomainException(ErrorCodes.ValidationError, "Unsupported thinking material schema version.");
                    var deck = new ThinkingDeck(card.Id);
                    // Validate source IDs before remapping; links never point into the source board.
                    var material = importCard.Thinking.Layers;
                    var sourceDeck = new ThinkingDeck(importCard.SourceId ?? card.Id);
                    sourceDeck.Replace(material);
                    if (sourceDeck.SchemaVersion > importCard.Thinking.SchemaVersion)
                        throw new DomainException(ErrorCodes.ValidationError, "Linked thinking cards require material schema version 2.");
                    deck.Replace(material.Select(layer => layer with
                    {
                        Items = layer.Items.Select(item => item.LinkedCardId.HasValue
                            ? item with { LinkedCardId = cardIds.TryGetValue(item.LinkedCardId.Value, out var linkedId) ? linkedId
                                : throw new DomainException(ErrorCodes.ValidationError, "Thinking link references a card outside this import.") }
                            : item).ToArray()
                    }).ToArray());
                    _thinkingDecks.AddForImport(deck);
                }
                cardsImported++;
            }

                CardHierarchy.Validate(importedCards);

                if (dto.Dependencies is { Count: > 0 })
                {
                    if (_dependencies is null)
                        throw new DomainException(ErrorCodes.InvalidOperation, "Dependency import is not available in this host.");
                    var graph = new BoardDependencies(board.Id);
                    var remapped = new List<CardDependency>();
                    foreach (var edge in dto.Dependencies)
                    {
                        if (edge is null || !cardIds.TryGetValue(edge.CardId, out var from) || !cardIds.TryGetValue(edge.DependsOnCardId, out var to))
                            throw new DomainException(ErrorCodes.ValidationError, "Dependencies must reference cards inside this import.");
                        remapped.Add(new CardDependency(from, to));
                    }
                    graph.Replace(remapped);
                    _dependencies.AddForImport(graph);
                }

                await _unitOfWork.SaveChangesAsync();
            if (preview) await _unitOfWork.RollbackTransactionAsync();
            else await _unitOfWork.CommitTransactionAsync();

            var result = new ImportResultDto(
                true,
                preview ? null : board.Id,
                null,
                columnsByName.Count,
                cardsImported,
                labelsByName.Count);

            return Result.Success(result);
        }
        catch (DomainException ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            return Result.Failure<ImportResultDto>(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            return Result.Failure<ImportResultDto>(ErrorCodes.UnexpectedError, $"Import failed: {ex.Message}");
        }
    }

    public async Task<Result<ImportResultDto>> ImportBoardFromJsonAsync(string json, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Result.Failure<ImportResultDto>(ErrorCodes.ValidationError, "Import JSON payload cannot be empty");

        try
        {
            var dto = TryDeserializeImportDto(json);
            if (dto is null)
                return Result.Failure<ImportResultDto>(ErrorCodes.ValidationError, "Failed to deserialize import data");

            return await ImportBoardAsync(dto, userId);
        }
        catch (JsonException ex)
        {
            return Result.Failure<ImportResultDto>(ErrorCodes.ValidationError, $"Invalid JSON format: {ex.Message}");
        }
    }

    private async Task<bool> CanUserReadBoardAsync(Board board, Guid userId)
    {
        if (_sandboxSettings.Enabled)
            return true;

        if (board.OwnerId == userId)
            return true;

        var access = await _unitOfWork.BoardAccesses.GetByBoardAndUserAsync(board.Id, userId);
        return access is not null && access.CanRead();
    }

    internal static ImportBoardDto? TryDeserializeImportDto(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("source", out var source))
            {
                var parsed = TryDeserializeImportDto(source.GetRawText());
                if (parsed is null) return null;
                var mappings = document.RootElement.TryGetProperty("assigneeMappings", out var mappingJson)
                    ? JsonSerializer.Deserialize<Dictionary<string, Guid?>>(mappingJson.GetRawText(), JsonOptions) : null;
                return parsed with { AssigneeMappings = mappings };
            }
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("format", out _))
            {
                var envelope = JsonSerializer.Deserialize<BoardExportEnvelope>(json, JsonOptions);
                if (envelope is not { Format: "taskdeck-board", Version: 2 or 3 or 4, Payload: not null }) return null;
                if (envelope.Version < 4 && (envelope.Payload.Cards ?? []).Any(c => c.Assignments is { Count: > 0 })) return null;
                return ConvertExportToImportDto(envelope.Payload);
            }
        }
        catch (JsonException) { return null; }
        ImportBoardDto? importDto = null;
        try
        {
            importDto = JsonSerializer.Deserialize<ImportBoardDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // Ignore and try export payload shape below.
        }

        if (importDto is not null && !string.IsNullOrWhiteSpace(importDto.Name))
            return importDto;

        ExportBoardDto? exportDto = null;
        try
        {
            exportDto = JsonSerializer.Deserialize<ExportBoardDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        return exportDto is null ? null : ConvertExportToImportDto(exportDto);
    }

    internal static ImportBoardDto ConvertExportToImportDto(ExportBoardDto exportDto)
    {
        if (exportDto.Board is null)
            throw new JsonException("Export payload is missing board metadata");

        var columns = exportDto.Columns?
            .OrderBy(c => c.Position)
            .Select(c => new ImportColumnDto(c.Name, c.Position, c.WipLimit))
            .ToList()
            ?? new List<ImportColumnDto>();

        var labels = new List<ImportLabelDto>();
        var seenLabelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in exportDto.Labels ?? Enumerable.Empty<LabelDto>())
        {
            if (!seenLabelNames.Add(label.Name))
            {
                throw new JsonException($"Export payload contains duplicate label name '{label.Name}'");
            }
            labels.Add(new ImportLabelDto(label.Name, label.ColorHex));
        }

        var columnNameById = new Dictionary<Guid, string>();
        var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in exportDto.Columns ?? Enumerable.Empty<ColumnDto>())
        {
            if (!columnNameById.TryAdd(column.Id, column.Name))
            {
                throw new JsonException($"Export payload contains duplicate column ID '{column.Id}'");
            }
            if (!columnNames.Add(column.Name))
            {
                throw new JsonException($"Export payload contains duplicate column name '{column.Name}'");
            }
        }

        var exportedCards = (exportDto.Cards ?? Enumerable.Empty<CardDto>()).ToList();
        var exportedCardIds = exportedCards.Select(card => card.Id).ToHashSet();
        if (exportedCardIds.Count != exportedCards.Count)
            throw new JsonException("Export payload contains duplicate card IDs");
        var thinkingByCard = new Dictionary<Guid, ThinkingMaterialDto>();
        foreach (var deck in exportDto.ThinkingDecks ?? [])
        {
            if (deck is null || deck.Material is null || !exportedCardIds.Contains(deck.CardId) || !thinkingByCard.TryAdd(deck.CardId, deck.Material))
                throw new JsonException("Export payload contains an invalid or duplicate thinking card reference");
        }

        var cards = new List<ImportCardDto>();
        foreach (var card in exportedCards)
        {
            if (card.Assignments?.Any(a => a is null || a.UserId == Guid.Empty || string.IsNullOrWhiteSpace(a.DisplayName)) == true)
                throw new JsonException("Export payload contains an invalid source assignee.");
            if (!columnNameById.TryGetValue(card.ColumnId, out var columnName))
            {
                throw new JsonException(
                    $"Export payload references unknown column ID '{card.ColumnId}' for card '{card.Title}'");
            }

            var labelNames = (card.Labels ?? Enumerable.Empty<LabelDto>())
                .Select(l => l.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            cards.Add(new ImportCardDto(
                card.Title,
                card.Description,
                columnName,
                card.Position,
                card.DueDate,
                labelNames,
                thinkingByCard.GetValueOrDefault(card.Id), card.Id, card.IsArchived, card.WorkItemType, card.ParentCardId,
                card.Assignments?.Select(a => new ImportSourceAssigneeDto(a.UserId.ToString(), a.DisplayName)).ToArray(),
                card.EstimatedEffortMinutes));
        }

        return new ImportBoardDto(
            exportDto.Board.Name,
            exportDto.Board.Description,
            columns,
            cards,
            labels,
            exportDto.Dependencies);
    }

    public static object ToPortablePayload(ExportBoardDto dto) => dto.Cards.Any(card => card.Assignments is { Count: > 0 })
        ? new BoardExportEnvelope("taskdeck-board", 4, dto)
        : dto.Cards.Any(card => card.ParentCardId.HasValue)
        ? new BoardExportEnvelope("taskdeck-board", 3, dto)
        : dto.Dependencies is { Count: > 0 } ? new BoardExportEnvelope("taskdeck-board", 2, dto) : dto;

    private static BoardDto MapToBoardDto(Board board)
    {
        return new BoardDto(
            board.Id,
            board.Name,
            board.Description,
            board.IsArchived,
            board.CreatedAt,
            board.UpdatedAt);
    }

    private static CardDto MapToCardDto(Card card)
    {
        var labels = card.CardLabels
            .Where(cl => cl.Label is not null)
            .Select(cl => new LabelDto(
                cl.Label.Id,
                cl.Label.BoardId,
                cl.Label.Name,
                cl.Label.ColorHex,
                cl.Label.CreatedAt,
                cl.Label.UpdatedAt))
            .ToList();

        return new CardDto(
            card.Id,
            card.BoardId,
            card.ColumnId,
            card.Title,
            card.Description,
            card.DueDate,
            card.IsBlocked,
            card.BlockReason,
            card.Position,
            labels,
            card.CreatedAt,
            card.UpdatedAt, card.IsArchived, card.WorkItemType.ToString(), card.ParentCardId,
            CardService.MapToDto(card).Assignments, card.EstimatedEffortMinutes);
    }
}
