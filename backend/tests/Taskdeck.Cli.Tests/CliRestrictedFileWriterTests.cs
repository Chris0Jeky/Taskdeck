using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

namespace Taskdeck.Cli.Tests;

/// <summary>
/// Covers issue #1262: the CLI's connector encryption key file must be created ATOMICALLY with
/// owner-only permissions, not written first and restricted afterwards. The pre-fix
/// <c>CliFirstRunBootstrapper.PersistKey</c> did <c>File.WriteAllText(tempPath, payload)</c> and only
/// then <c>File.SetUnixFileMode(tempPath, ...)</c> on Unix (a TOCTOU window where the key sits at the
/// umask-derived mode, typically 0644) with no Windows protection at all (the temp file inherited the
/// directory DACL, typically granting BUILTIN\Users read).
///
/// Mirrors the API-side coverage added by PR #1267 for #1264
/// (backend/tests/Taskdeck.Api.Tests/FirstRun/FirstRunBootstrapperTests.cs).
/// </summary>
public class CliRestrictedFileWriterTests
{
    [Fact]
    public void PersistKey_WritesTheKeyViaAtomicRestrictedCreate_StructuralCheck()
    {
        // The behavioral tests cannot distinguish atomic create-with-permissions from a regression back
        // to create-then-restrict (the final file state is identical on NTFS), so pin the construction
        // structurally -- the same shape as the API's #1264 structural test.
        var source = File.ReadAllText(FindCliSourceFile("CliFirstRunBootstrapper.cs"));

        Assert.Contains("RestrictedFileWriter.WriteRestrictedFile(tempPath, payload)", source);
        // The pre-#1262 create-then-restrict sequence must not come back.
        Assert.DoesNotContain("File.WriteAllText(tempPath", source);
        Assert.DoesNotContain("SetUnixFileMode(tempPath", source);
    }

    [Fact]
    public void EnsureKeyOnDisk_LockdownSurvivesAtomicMove()
    {
        // End-to-end property: PersistKey stages the key in a restricted temp file and then
        // File.Move(overwrite)s it into place. Assert the owner-only lockdown survives that same-directory
        // atomic rename, on the real file the CLI reads its connector key from.
        var dir = Path.Combine(Path.GetTempPath(), $"td-cli-key-move-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var localConfigPath = Path.Combine(dir, "appsettings.local.json");
        try
        {
            var key = CliFirstRunBootstrapper.EnsureKeyOnDisk(localConfigPath);

            Assert.False(string.IsNullOrWhiteSpace(key));
            Assert.True(File.Exists(localConfigPath), "the bootstrap should have persisted the key file");
            AssertOwnerOnly(localConfigPath);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static void AssertOwnerOnly(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var security = new FileInfo(path).GetAccessControl();
            Assert.True(
                security.AreAccessRulesProtected,
                "inheritance should be disabled so the directory's default ACEs (e.g. BUILTIN\\Users read) do not apply");

            using var identity = WindowsIdentity.GetCurrent();
            var currentSid = identity.User;
            var rules = security.GetAccessRules(
                includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));

            // Iterate inside the OperatingSystem.IsWindows() guard (not a lambda) so the platform
            // analyzer recognizes the guard for the Windows-only rule members.
            Assert.True(rules.Count > 0, "the file should have at least one explicit access rule");
            foreach (FileSystemAccessRule rule in rules)
            {
                Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
                Assert.Equal(currentSid, rule.IdentityReference);
            }
        }
        else
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(path));
        }
    }

    private static string FindCliSourceFile(string fileName)
    {
        // Walk up from the test output directory to find the repo source.
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "backend", "src", "Taskdeck.Cli", fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException(
            $"Could not locate {fileName} by walking up from {AppContext.BaseDirectory}");
    }
}
