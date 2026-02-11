using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Service interface for exporting and importing boards.
/// SCAFFOLDING: Implementation pending.
/// </summary>
public interface IExportImportService
{
    Task<Result<ExportBoardDto>> ExportBoardAsync(Guid boardId, Guid userId);
    Task<Result<string>> ExportBoardToJsonAsync(Guid boardId, Guid userId);
    Task<Result<ImportResultDto>> ImportBoardAsync(ImportBoardDto dto, Guid userId);
    Task<Result<ImportResultDto>> ImportBoardFromJsonAsync(string json, Guid userId);
    Task<Result<byte[]>> ExportDatabaseAsync(Guid userId);
    Task<Result> ImportDatabaseAsync(byte[] dbFile, Guid userId);
}
