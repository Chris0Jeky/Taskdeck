using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class ExportImportService : IExportImportService
{
    private readonly IUnitOfWork _unitOfWork;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public ExportImportService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
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

            var exportDto = new ExportBoardDto(
                boardDto,
                columns,
                cards,
                labels,
                accessDtos,
                DateTimeOffset.UtcNow,
                requestingUser.Username);

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

        var json = JsonSerializer.Serialize(exportResult.Value, JsonOptions);
        return Result.Success(json);
    }

    public async Task<Result<ImportResultDto>> ImportBoardAsync(ImportBoardDto dto, Guid userId)
    {
        try
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null)
                return Result.Failure<ImportResultDto>(ErrorCodes.NotFound, $"User with ID {userId} not found");

            await _unitOfWork.BeginTransactionAsync();

            var labels = dto.Labels ?? Enumerable.Empty<ImportLabelDto>();
            var columns = dto.Columns ?? Enumerable.Empty<ImportColumnDto>();
            var cards = dto.Cards ?? Enumerable.Empty<ImportCardDto>();

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
            var cardsImported = 0;
            foreach (var importCard in cards.OrderBy(c => c.Position))
            {
                if (!columnsByName.TryGetValue(importCard.ColumnName, out var column))
                    throw new DomainException(ErrorCodes.ValidationError, $"Column '{importCard.ColumnName}' referenced by card '{importCard.Title}' was not found");

                var card = new Card(board.Id, column.Id, importCard.Title, importCard.Description, importCard.DueDate, importCard.Position);
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

                await _unitOfWork.Cards.AddAsync(card);
                cardsImported++;
            }

            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitTransactionAsync();

            var result = new ImportResultDto(
                true,
                board.Id,
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

    public Task<Result<byte[]>> ExportDatabaseAsync(Guid userId)
    {
        return Task.FromResult(Result.Failure<byte[]>(ErrorCodes.ValidationError, "Database export is not yet implemented"));
    }

    public Task<Result> ImportDatabaseAsync(byte[] dbFile, Guid userId)
    {
        return Task.FromResult(Result.Failure(ErrorCodes.ValidationError, "Database import is not yet implemented"));
    }

    private async Task<bool> CanUserReadBoardAsync(Board board, Guid userId)
    {
        if (board.OwnerId is null || board.OwnerId == userId)
            return true;

        var access = await _unitOfWork.BoardAccesses.GetByBoardAndUserAsync(board.Id, userId);
        return access is not null && access.CanRead();
    }

    private static ImportBoardDto? TryDeserializeImportDto(string json)
    {
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

    private static ImportBoardDto ConvertExportToImportDto(ExportBoardDto exportDto)
    {
        if (exportDto.Board is null)
            throw new JsonException("Export payload is missing board metadata");

        var columns = exportDto.Columns?
            .OrderBy(c => c.Position)
            .Select(c => new ImportColumnDto(c.Name, c.Position, c.WipLimit))
            .ToList()
            ?? new List<ImportColumnDto>();

        var labels = exportDto.Labels?
            .Select(l => new ImportLabelDto(l.Name, l.ColorHex))
            .ToList()
            ?? new List<ImportLabelDto>();

        var columnNameById = (exportDto.Columns ?? Enumerable.Empty<ColumnDto>())
            .ToDictionary(c => c.Id, c => c.Name);

        var cards = new List<ImportCardDto>();
        foreach (var card in exportDto.Cards ?? Enumerable.Empty<CardDto>())
        {
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
                labelNames));
        }

        return new ImportBoardDto(
            exportDto.Board.Name,
            exportDto.Board.Description,
            columns,
            cards,
            labels);
    }

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
            card.UpdatedAt);
    }
}
