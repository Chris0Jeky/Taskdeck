using Microsoft.Data.Sqlite;
using Xunit;

namespace Taskdeck.Integration.Tests;

public sealed class SQLiteNativeVersionTests
{
    [Fact]
    public void NativeSQLiteVersionMeetsPatchedSecurityFloor()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";

        var versionText = Assert.IsType<string>(command.ExecuteScalar());
        var isVersion = Version.TryParse(versionText, out var version);

        Assert.True(isVersion, $"SQLite returned an unparsable version: {versionText}.");
        Assert.True(
            version!.CompareTo(new Version(3, 50, 2)) >= 0,
            $"SQLite {version} is below the patched security floor of 3.50.2.");
    }
}
