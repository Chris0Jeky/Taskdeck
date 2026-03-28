using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Handles board-level JSON export and import operations.
/// </summary>
public interface IBoardJsonExportImportService
{
    Task<Result<ExportBoardDto>> ExportBoardAsync(Guid boardId, Guid userId);
    Task<Result<string>> ExportBoardToJsonAsync(Guid boardId, Guid userId);
    Task<Result<ImportResultDto>> ImportBoardAsync(ImportBoardDto dto, Guid userId);
    Task<Result<ImportResultDto>> ImportBoardFromJsonAsync(string json, Guid userId);
}
