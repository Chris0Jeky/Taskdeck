using Xunit;

namespace Taskdeck.Api.Tests.FirstRun;

public sealed class ReleaseDesktopWorkflowTests
{
    [Fact]
    public void ReleaseDesktopSmoke_ProvesZeroConfigDurabilityAndLegacyMigration()
    {
        var workflow = File.ReadAllText(FindRepoFile(".github", "workflows", "release-desktop.yml"));

        Assert.DoesNotContain("Jwt__SecretKey=", workflow);
        Assert.DoesNotContain("Connectors__EncryptionKey=", workflow);
        Assert.DoesNotContain("ConnectionStrings__DefaultConnection=", workflow);
        Assert.Equal(3, CountOccurrences(
            workflow,
            "env -u CI -u TF_BUILD -u GITHUB_ACTIONS -u TASKDECK_HEADLESS"));
        Assert.Contains("LOCALAPPDATA=\"${APP_LOCALAPPDATA}\"", workflow);
        Assert.Contains("HOME=\"${APP_HOME}\"", workflow);
        Assert.Contains("XDG_DATA_HOME=\"${APP_XDG_DATA_HOME}\"", workflow);
        Assert.Contains("CONFIG_DIGEST_ONE", workflow);
        Assert.Contains("CONFIG_DIGEST_TWO", workflow);
        Assert.Contains("CONFIG_DIGEST_THREE", workflow);
        Assert.Contains("DB_PATH_ONE", workflow);
        Assert.Contains("DB_PATH_TWO", workflow);
        Assert.Contains("DB_PATH_THREE", workflow);
        Assert.Contains("canonicalize_db_path", workflow);
        Assert.Contains("assert_sqlite_database", workflow);
        Assert.Contains("53514c69746520666f726d6174203300", workflow);
        Assert.Contains("outside the isolated smoke profile", workflow);
        Assert.Contains("-name 'taskdeck.db*'", workflow);
        Assert.Contains("CONNECTOR_KEY_ONE", workflow);
        Assert.Contains("CONNECTOR_KEY_THREE", workflow);
        Assert.Contains("LEGACY_CONFIG", workflow);
        Assert.Contains("rm -f \"$DURABLE_CONFIG\"", workflow);
        Assert.Contains("Legacy executable-local config was not retained", workflow);
        Assert.Contains("RELOCATED_DIR", workflow);
        Assert.Contains("${RELOCATED_DIR}/appsettings.local.json", workflow);
        Assert.Contains("${PUBLISH_DIR}/appsettings.local.json", workflow);

        Assert.Contains("stop_app() {", workflow);
        Assert.Equal(4, CountOccurrences(workflow, "stop_app"));
        Assert.Contains("for _ in {1..40}; do", workflow);
        Assert.Contains("kill -9 \"${pid}\"", workflow);
        Assert.Contains("wait \"${pid}\"", workflow);
        Assert.DoesNotContain("wait \"${APP_PID}\"", workflow);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string FindRepoFile(params string[] segments)
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            var candidate = segments.Aggregate(directory, Path.Combine);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(segments)}.");
    }
}
