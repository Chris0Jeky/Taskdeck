namespace Taskdeck.Application.Services;

public class DatabaseExportImportSettings
{
    public const int DefaultMaxImportBytes = 50 * 1024 * 1024;

    public string? ConnectionString { get; set; }
    public int MaxImportBytes { get; set; } = DefaultMaxImportBytes;
}
