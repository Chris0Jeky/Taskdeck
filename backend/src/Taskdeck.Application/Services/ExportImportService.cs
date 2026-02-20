using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class ExportImportService : IExportImportService
{
    private const int SqliteHeaderLength = 16;
    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    private readonly IUnitOfWork _unitOfWork;
    private readonly DevelopmentSandboxSettings _sandboxSettings;
    private readonly DatabaseExportImportSettings _databaseSettings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public ExportImportService(
        IUnitOfWork unitOfWork,
        DevelopmentSandboxSettings? sandboxSettings = null,
        DatabaseExportImportSettings? databaseSettings = null)
    {
        _unitOfWork = unitOfWork;
        _sandboxSettings = sandboxSettings ?? new DevelopmentSandboxSettings();
        _databaseSettings = databaseSettings ?? new DatabaseExportImportSettings();
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

    public async Task<Result<byte[]>> ExportDatabaseAsync(Guid userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null)
            return Result.Failure<byte[]>(ErrorCodes.NotFound, $"User with ID {userId} not found");

        if (!_sandboxSettings.Enabled)
            return Result.Failure<byte[]>(ErrorCodes.Forbidden, "Database export is only allowed when DevelopmentSandbox is enabled");

        var databasePathResult = ResolveDatabasePath();
        if (!databasePathResult.IsSuccess)
            return Result.Failure<byte[]>(databasePathResult.ErrorCode, databasePathResult.ErrorMessage);

        var databasePath = databasePathResult.Value;
        if (!File.Exists(databasePath))
            return Result.Failure<byte[]>(ErrorCodes.NotFound, $"Database file was not found at '{databasePath}'");

        try
        {
            var bytes = await File.ReadAllBytesAsync(databasePath);
            if (bytes.Length == 0)
                return Result.Failure<byte[]>(ErrorCodes.ValidationError, "Database export produced an empty file");

            return Result.Success(bytes);
        }
        catch (Exception ex)
        {
            return Result.Failure<byte[]>(ErrorCodes.UnexpectedError, $"Database export failed: {ex.Message}");
        }
    }

    public async Task<Result> ImportDatabaseAsync(byte[] dbFile, Guid userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null)
            return Result.Failure(ErrorCodes.NotFound, $"User with ID {userId} not found");

        if (!_sandboxSettings.Enabled)
            return Result.Failure(ErrorCodes.Forbidden, "Database import is only allowed when DevelopmentSandbox is enabled");

        if (dbFile == null || dbFile.Length == 0)
            return Result.Failure(ErrorCodes.ValidationError, "Database import payload cannot be empty");

        var maxImportBytes = Math.Clamp(
            _databaseSettings.MaxImportBytes,
            1 * 1024 * 1024,
            500 * 1024 * 1024);
        if (dbFile.Length > maxImportBytes)
            return Result.Failure(
                ErrorCodes.ValidationError,
                $"Database import payload exceeds max size of {maxImportBytes} bytes");

        if (!HasSqliteSignature(dbFile))
            return Result.Failure(ErrorCodes.ValidationError, "Database import payload is not a valid SQLite file");

        var databasePathResult = ResolveDatabasePath();
        if (!databasePathResult.IsSuccess)
            return Result.Failure(databasePathResult.ErrorCode, databasePathResult.ErrorMessage);

        var databasePath = databasePathResult.Value;
        var databaseDirectory = Path.GetDirectoryName(databasePath);
        if (string.IsNullOrWhiteSpace(databaseDirectory))
            return Result.Failure(ErrorCodes.ValidationError, "Unable to resolve database directory");

        Directory.CreateDirectory(databaseDirectory);

        var stagingPath = Path.Combine(databaseDirectory, $".taskdeck-db-import-{Guid.NewGuid():N}.tmp");
        var backupPath = Path.Combine(databaseDirectory, $".taskdeck-db-backup-{Guid.NewGuid():N}.bak");
        var backupCreated = false;

        try
        {
            await File.WriteAllBytesAsync(stagingPath, dbFile);

            if (File.Exists(databasePath))
            {
                File.Copy(databasePath, backupPath, overwrite: true);
                backupCreated = true;
            }

            File.Copy(stagingPath, databasePath, overwrite: true);
            return Result.Success();
        }
        catch (IOException ex)
        {
            if (backupCreated && File.Exists(backupPath))
            {
                try
                {
                    File.Copy(backupPath, databasePath, overwrite: true);
                }
                catch
                {
                    // Intentionally swallow backup restore failures to preserve original error.
                }
            }

            return Result.Failure(
                ErrorCodes.InvalidOperation,
                $"Database import failed because the database file is in use or locked. Ensure no active connections and retry. Details: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            if (backupCreated && File.Exists(backupPath))
            {
                try
                {
                    File.Copy(backupPath, databasePath, overwrite: true);
                }
                catch
                {
                    // Intentionally swallow backup restore failures to preserve original error.
                }
            }

            return Result.Failure(
                ErrorCodes.InvalidOperation,
                $"Database import failed due to file access restrictions. Ensure no active connections and retry. Details: {ex.Message}");
        }
        catch (Exception ex)
        {
            if (backupCreated && File.Exists(backupPath))
            {
                try
                {
                    File.Copy(backupPath, databasePath, overwrite: true);
                }
                catch
                {
                    // Intentionally swallow backup restore failures to preserve original error.
                }
            }

            return Result.Failure(ErrorCodes.UnexpectedError, $"Database import failed: {ex.Message}");
        }
        finally
        {
            TryDeleteFile(stagingPath);
            TryDeleteFile(backupPath);
        }
    }

    private Result<string> ResolveDatabasePath()
    {
        var connectionString = _databaseSettings.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
            return Result.Failure<string>(ErrorCodes.ValidationError, "Database connection string is not configured");

        var dataSource = TryGetConnectionValue(connectionString, "Data Source")
            ?? TryGetConnectionValue(connectionString, "DataSource");
        if (string.IsNullOrWhiteSpace(dataSource))
            return Result.Failure<string>(ErrorCodes.ValidationError, "Database connection string is missing Data Source");

        var trimmedDataSource = dataSource.Trim().Trim('"');
        if (trimmedDataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
            return Result.Failure<string>(ErrorCodes.ValidationError, "Database export/import is not supported for in-memory data sources");

        if (trimmedDataSource.StartsWith("|DataDirectory|", StringComparison.OrdinalIgnoreCase))
        {
            trimmedDataSource = Path.Combine(
                AppContext.BaseDirectory,
                trimmedDataSource["|DataDirectory|".Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        var fullPath = Path.IsPathRooted(trimmedDataSource)
            ? Path.GetFullPath(trimmedDataSource)
            : Path.GetFullPath(trimmedDataSource, Directory.GetCurrentDirectory());

        return Result.Success(fullPath);
    }

    private static string? TryGetConnectionValue(string connectionString, string key)
    {
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == segment.Length - 1)
                continue;

            var segmentKey = segment[..separatorIndex].Trim();
            if (!segmentKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            return segment[(separatorIndex + 1)..].Trim();
        }

        return null;
    }

    private static bool HasSqliteSignature(byte[] bytes)
    {
        if (bytes.Length < SqliteHeaderLength)
            return false;

        for (var i = 0; i < SqliteHeaderLength; i++)
        {
            if (bytes[i] != SqliteHeader[i])
                return false;
        }

        return true;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cleanup failures should not fail business operation paths.
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

        var columnNameById = new Dictionary<Guid, string>();
        foreach (var column in exportDto.Columns ?? Enumerable.Empty<ColumnDto>())
        {
            if (!columnNameById.TryAdd(column.Id, column.Name))
            {
                throw new JsonException($"Export payload contains duplicate column ID '{column.Id}'");
            }
        }

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
