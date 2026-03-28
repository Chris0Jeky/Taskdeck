using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Facade that delegates to IBoardJsonExportImportService and
/// IDatabaseFileExportImportService. Preserves the original
/// IExportImportService contract for backward compatibility.
/// </summary>
public class ExportImportService : IExportImportService
{
    private readonly IBoardJsonExportImportService _boardJsonService;
    private readonly IDatabaseFileExportImportService _databaseFileService;

    public ExportImportService(
        IBoardJsonExportImportService boardJsonService,
        IDatabaseFileExportImportService databaseFileService)
    {
        _boardJsonService = boardJsonService;
        _databaseFileService = databaseFileService;
    }

    /// <summary>
    /// Backward-compatible convenience constructor that creates the
    /// underlying services directly. Intended for use in tests and
    /// non-DI contexts only.
    /// </summary>
    public ExportImportService(
        IUnitOfWork unitOfWork,
        DevelopmentSandboxSettings? sandboxSettings = null,
        DatabaseExportImportSettings? databaseSettings = null)
        : this(
            new BoardJsonExportImportService(unitOfWork, sandboxSettings),
            new DatabaseFileExportImportService(unitOfWork, sandboxSettings, databaseSettings))
    {
    }

    public Task<Result<ExportBoardDto>> ExportBoardAsync(Guid boardId, Guid userId)
        => _boardJsonService.ExportBoardAsync(boardId, userId);

    public Task<Result<string>> ExportBoardToJsonAsync(Guid boardId, Guid userId)
        => _boardJsonService.ExportBoardToJsonAsync(boardId, userId);

    public Task<Result<ImportResultDto>> ImportBoardAsync(ImportBoardDto dto, Guid userId)
        => _boardJsonService.ImportBoardAsync(dto, userId);

    public Task<Result<ImportResultDto>> ImportBoardFromJsonAsync(string json, Guid userId)
        => _boardJsonService.ImportBoardFromJsonAsync(json, userId);

    public Task<Result<byte[]>> ExportDatabaseAsync(Guid userId)
        => _databaseFileService.ExportDatabaseAsync(userId);

    public Task<Result> ImportDatabaseAsync(byte[] dbFile, Guid userId)
        => _databaseFileService.ImportDatabaseAsync(dbFile, userId);
}
