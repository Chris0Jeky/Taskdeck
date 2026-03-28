using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class DatabaseFileExportImportService : IDatabaseFileExportImportService
{
    private const int SqliteHeaderLength = 16;
    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    private readonly IUnitOfWork _unitOfWork;
    private readonly DevelopmentSandboxSettings _sandboxSettings;
    private readonly DatabaseExportImportSettings _databaseSettings;

    public DatabaseFileExportImportService(
        IUnitOfWork unitOfWork,
        DevelopmentSandboxSettings? sandboxSettings = null,
        DatabaseExportImportSettings? databaseSettings = null)
    {
        _unitOfWork = unitOfWork;
        _sandboxSettings = sandboxSettings ?? new DevelopmentSandboxSettings();
        _databaseSettings = databaseSettings ?? new DatabaseExportImportSettings();
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

    internal Result<string> ResolveDatabasePath()
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

    internal static string? TryGetConnectionValue(string connectionString, string key)
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

    internal static bool HasSqliteSignature(byte[] bytes)
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
}
