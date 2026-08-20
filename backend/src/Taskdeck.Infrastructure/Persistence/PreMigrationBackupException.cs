namespace Taskdeck.Infrastructure.Persistence;

/// <summary>
/// Thrown when the pre-migration SQLite snapshot could not be written, which blocks the
/// migration (fail-closed, #1803). The message is written to be actionable for a local-first
/// user reading a crashed startup log, not just for a developer.
/// </summary>
public sealed class PreMigrationBackupException : Exception
{
    public PreMigrationBackupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public PreMigrationBackupException(string message)
        : base(message)
    {
    }
}
