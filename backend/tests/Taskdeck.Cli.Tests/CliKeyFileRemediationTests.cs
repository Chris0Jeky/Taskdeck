using System.Text;
using Xunit;

namespace Taskdeck.Cli.Tests;

/// <summary>
/// Covers issue #2667 item 1: forward remediation of a connector encryption key file that a build
/// older than #1262 already wrote unprotected. PR #2665 made NEW key files born owner-only, but
/// <c>CliFirstRunBootstrapper.EnsureKeyOnDisk</c> returned as soon as it found an existing key, so an
/// upgraded install kept the directory's inherited Windows DACL (typically BUILTIN\Users read) or the
/// umask-derived Unix mode forever.
///
/// Mirrors the API-side #1241 coverage
/// (backend/tests/Taskdeck.Api.Tests/FirstRun/FirstRunBootstrapperTests.cs).
///
/// In the "Console Tests" collection because one test replaces <see cref="Console.Error"/>, which is
/// process-global.
/// </summary>
[Collection("Console Tests")]
public class CliKeyFileRemediationTests
{
    // Known-valid base64 256-bit key (32 zero bytes).
    private const string PersistedKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    [Fact]
    public void EnsureKeyOnDisk_ExistingUnrestrictedKeyFile_IsRestrictedToCurrentUser()
    {
        // Write the key file the OLD way -- File.WriteAllBytes into a fresh directory, so on Windows it
        // inherits that directory's DACL and on Unix it lands at the umask-derived mode (typically 0644).
        // The bootstrap must hand back the persisted key AND lock the file down, without touching a byte
        // of its content.
        var dir = Path.Combine(Path.GetTempPath(), $"td-cli-remediate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var localConfigPath = Path.Combine(dir, "appsettings.local.json");
        var payload = Encoding.UTF8.GetBytes(
            "{\n  \"Connectors\": {\n    \"EncryptionKey\": \"" + PersistedKey + "\"\n  },\n" +
            "  \"Unrelated\": { \"Kept\": true }\n}");
        try
        {
            File.WriteAllBytes(localConfigPath, payload);

            var key = CliFirstRunBootstrapper.EnsureKeyOnDisk(localConfigPath);

            Assert.Equal(PersistedKey, key);
            Assert.Equal(payload, File.ReadAllBytes(localConfigPath));
            CliRestrictedFileWriterTests.AssertOwnerOnly(localConfigPath);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void RestrictFileToCurrentUser_LocksFileToCurrentUserOnly()
    {
        // Behavioral test for the copied #1241 lockdown helper. Runs on both platforms (each CI runner
        // covers its own branch): on Unix it asserts 0600; on Windows it asserts the DACL is protected
        // (inheritance disabled) with the current user as the only granted principal.
        var path = Path.Combine(Path.GetTempPath(), $"td-cli-acl-{Guid.NewGuid():N}.tmp");
        try
        {
            File.Create(path).Dispose();

            RestrictedFileWriter.RestrictFileToCurrentUser(path);

            CliRestrictedFileWriterTests.AssertOwnerOnly(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RestrictExistingKeyFileAt_MissingFile_DoesNothing()
    {
        // Never touch a path that does not exist: the first-run path calls this before it knows whether a
        // file is there, and creating or probing one would be a side effect the caller did not ask for.
        var path = Path.Combine(
            Path.GetTempPath(), $"td-cli-absent-{Guid.NewGuid():N}", "appsettings.local.json");
        var called = false;

        CliFirstRunBootstrapper.RestrictExistingKeyFileAt(path, _ => called = true);

        Assert.False(called, "the remediation must not run against a file that does not exist");
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void RestrictExistingKeyFileAt_WhenRestrictionFails_WarnsOnStderrAndDoesNotThrow()
    {
        // The remediation is best-effort: a locked-down host where the DACL/mode cannot be changed must
        // still get its connector key back. One stderr warning naming the path, no exception, stdout
        // untouched (the CLI keeps stdout clean JSON).
        var path = Path.Combine(Path.GetTempPath(), $"td-cli-remediate-fail-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(path, "{}");
        var originalErr = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            CliFirstRunBootstrapper.RestrictExistingKeyFileAt(
                path, _ => throw new IOException("simulated lockdown failure"));
        }
        finally
        {
            Console.SetError(originalErr);
            if (File.Exists(path)) File.Delete(path);
        }

        var warning = stderr.ToString();
        Assert.Contains("[CliFirstRun] WARNING", warning);
        Assert.Contains(path, warning);
        Assert.Contains("simulated lockdown failure", warning);
    }

    [Fact]
    public void EnsureKeyOnDisk_ExistingKeyPath_CallsTheRemediation_StructuralCheck()
    {
        // The behavioral test above cannot distinguish "remediated on the existing-key path" from
        // "remediated somewhere else that happens to run first", and it cannot see a future edit that
        // moves the call behind the early return. Pin the call site: it must sit between reading the
        // existing config and returning the existing key.
        var source = File.ReadAllText(
            CliRestrictedFileWriterTests.FindCliSourceFile("CliFirstRunBootstrapper.cs"));

        const string readMarker = "var existing = ReadExisting(localConfigPath);";
        const string returnMarker = "return existing.Key!;";
        var readIndex = source.IndexOf(readMarker, StringComparison.Ordinal);
        var returnIndex = source.IndexOf(returnMarker, StringComparison.Ordinal);

        Assert.True(readIndex >= 0, $"could not find '{readMarker}' in CliFirstRunBootstrapper.cs");
        Assert.True(returnIndex > readIndex, $"could not find '{returnMarker}' after '{readMarker}'");

        var existingKeyPath = source[readIndex..returnIndex];
        Assert.Contains("RestrictExistingKeyFileAt(localConfigPath);", existingKeyPath);
    }
}
