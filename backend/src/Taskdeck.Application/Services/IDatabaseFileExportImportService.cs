using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

/// <summary>
/// Handles SQLite database file-level export and import operations.
/// </summary>
public interface IDatabaseFileExportImportService
{
    Task<Result<byte[]>> ExportDatabaseAsync(Guid userId);
    Task<Result> ImportDatabaseAsync(byte[] dbFile, Guid userId);
}
