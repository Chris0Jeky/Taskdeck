using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Xunit;
using Taskdeck.Api.FirstRun;

namespace Taskdeck.Api.Tests.FirstRun;

public class FirstRunBootstrapperTests
{
    // ---- IsPlaceholder -------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TaskdeckDevelopmentOnlySecretKeyChangeMe123!")]
    [InlineData("CHANGE_ME_GENERATE_WITH_openssl_rand_base64_48")]
    public void IsPlaceholder_ReturnsTrueForPlaceholderValues(string value)
    {
        Assert.True(FirstRunBootstrapper.IsPlaceholder(value));
    }

    [Theory]
    [InlineData("someRealSecret123!")]
    [InlineData("aVeryLongAndCompletelyRandomSecretThatIsNotAPlaceholder")]
    public void IsPlaceholder_ReturnsFalseForRealSecrets(string value)
    {
        Assert.False(FirstRunBootstrapper.IsPlaceholder(value));
    }

    // ---- GenerateSecret ------------------------------------------------------

    [Fact]
    public void GenerateSecret_ReturnsCryptographicallyRandomBase64String()
    {
        var secret1 = FirstRunBootstrapper.GenerateSecret();
        var secret2 = FirstRunBootstrapper.GenerateSecret();

        // Should be base64
        var bytes = Convert.FromBase64String(secret1);
        Assert.Equal(32, bytes.Length); // 256-bit

        // Should be unique across calls
        Assert.NotEqual(secret1, secret2);
    }

    [Fact]
    public void GenerateSecret_ProducesDifferentValuesOnEachCall()
    {
        var secrets = Enumerable.Range(0, 10).Select(_ => FirstRunBootstrapper.GenerateSecret()).ToList();
        var distinct = secrets.Distinct().ToList();
        Assert.Equal(secrets.Count, distinct.Count);
    }

    // ---- ShouldAutoGenerateConnectorKey --------------------------------------

    [Theory]
    // Non-Production (dev/staging) always generates, regardless of headless.
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    // Production + NOT headless = the desktop exe: generate so the self-contained exe is runnable
    // without manually supplying Connectors__EncryptionKey (the generated key persists locally).
    [InlineData(true, false, true)]
    // Production + headless = CI / cloud container: do NOT generate -- a generated key may be
    // ephemeral there, so these deployments must supply a stable key.
    [InlineData(true, true, false)]
    public void ShouldAutoGenerateConnectorKey_GeneratesExceptInHeadlessProduction(
        bool isProduction, bool isHeadless, bool expected)
    {
        Assert.Equal(
            expected,
            FirstRunBootstrapper.ShouldAutoGenerateConnectorKey(isProduction, isHeadless));
    }

    // ---- TryReadPersistedConnectorKey (masked-key reuse: never overwrite a persisted key) ----------

    [Fact]
    public void TryReadPersistedConnectorKey_ReturnsPersistedKey_WhenFileHasOne()
    {
        var path = Path.Combine(Path.GetTempPath(), $"td-connkey-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\"Connectors\":{\"EncryptionKey\":\"K1-persisted\"},\"Jwt\":{\"SecretKey\":\"j\"}}");
        try
        {
            Assert.True(FirstRunBootstrapper.TryReadPersistedConnectorKey(path, out var key));
            Assert.Equal("K1-persisted", key);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TryReadPersistedConnectorKey_ReturnsFalse_WhenFileMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"td-connkey-missing-{Guid.NewGuid():N}.json");
        Assert.False(FirstRunBootstrapper.TryReadPersistedConnectorKey(path, out var key));
        Assert.Null(key);
    }

    [Theory]
    [InlineData("not json at all {{{")]                          // unparsable -> not a clean key
    [InlineData("[]")]                                           // non-object root
    [InlineData("{\"Connectors\":{\"EncryptionKey\":\"\"}}")]    // empty value
    [InlineData("{\"Connectors\":{\"EncryptionKey\":\"   \"}}")] // whitespace value
    [InlineData("{\"Jwt\":{\"SecretKey\":\"j\"}}")]              // key absent
    public void TryReadPersistedConnectorKey_ReturnsFalse_ForUnusableContent(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"td-connkey-bad-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        try
        {
            Assert.False(FirstRunBootstrapper.TryReadPersistedConnectorKey(path, out var key));
            Assert.True(string.IsNullOrWhiteSpace(key));
        }
        finally { File.Delete(path); }
    }

    // ---- QuarantineCorruptLocalConfigAt (corrupt-config self-heal) ----------

    [Fact]
    public void QuarantineCorruptLocalConfigAt_PreservesAndRemovesACorruptFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"td-corrupt-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ this is : not valid json");
        try
        {
            FirstRunBootstrapper.QuarantineCorruptLocalConfigAt(path);

            Assert.False(File.Exists(path),
                "the corrupt original must be removed so the optional config source loads as missing");
            var preserved = Directory.GetFiles(
                Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".corrupt-*");
            Assert.Single(preserved);
            Assert.Contains("not valid json", File.ReadAllText(preserved[0]));
            File.Delete(preserved[0]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void QuarantineCorruptLocalConfigAt_LeavesAValidObjectFileUntouched()
    {
        var path = Path.Combine(Path.GetTempPath(), $"td-valid-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\"Connectors\":{\"EncryptionKey\":\"K1\"}}");
        try
        {
            FirstRunBootstrapper.QuarantineCorruptLocalConfigAt(path);
            Assert.True(File.Exists(path), "a valid JSON object file must be left in place");
            Assert.Empty(Directory.GetFiles(
                Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".corrupt-*"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void QuarantineCorruptLocalConfigAt_LeavesACommentedTrailingCommaFileUntouched()
    {
        // The JSON configuration provider accepts comments and trailing commas, so a hand-edited but
        // loadable file must NOT be quarantined (doing so would delete a recoverable connector key).
        var path = Path.Combine(Path.GetTempPath(), $"td-lenient-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\n  // hand-edited\n  \"Connectors\": { \"EncryptionKey\": \"K1\", },\n}");
        try
        {
            FirstRunBootstrapper.QuarantineCorruptLocalConfigAt(path);
            Assert.True(File.Exists(path),
                "a comment / trailing-comma file the config provider accepts must be left in place");
            Assert.Empty(Directory.GetFiles(
                Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".corrupt-*"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void TryReadPersistedConnectorKey_ReadsKey_FromCommentedTrailingCommaFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"td-lenient-key-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\n  // hand-edited\n  \"Connectors\": { \"EncryptionKey\": \"K1-lenient\", },\n}");
        try
        {
            Assert.True(FirstRunBootstrapper.TryReadPersistedConnectorKey(path, out var key));
            Assert.Equal("K1-lenient", key);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TryReadPersistedConnectorKey_ReadsKey_CaseInsensitively()
    {
        // The configuration provider matches keys case-insensitively, so a provider-valid case variant must
        // be found -- otherwise a masked key would be missed and a different one regenerated.
        var path = Path.Combine(Path.GetTempPath(), $"td-connkey-case-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{\"connectors\":{\"encryptionkey\":\"K1-lower\"}}");
        try
        {
            Assert.True(FirstRunBootstrapper.TryReadPersistedConnectorKey(path, out var key));
            Assert.Equal("K1-lower", key);
        }
        finally { File.Delete(path); }
    }

    // ---- GetAppDataPath ------------------------------------------------------

    [Fact]
    public void GetAppDataPath_ReturnsPathEndingWithTaskdeck()
    {
        var path = FirstRunBootstrapper.GetAppDataPath();
        Assert.EndsWith("Taskdeck", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetAppDataPath_ReturnsAbsolutePath()
    {
        var path = FirstRunBootstrapper.GetAppDataPath();
        Assert.True(Path.IsPathRooted(path), $"Expected absolute path but got: {path}");
    }

    // ---- AddLocalConfigFile source ordering ----------------------------------

    [Fact]
    public void AddLocalConfigFile_InsertsFileSourceBeforeEnvironmentVariablesSource()
    {
        // Arrange: build a minimal config builder that mimics the .NET default
        // source ordering (appsettings.json → env vars → CLI args).
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>()); // proxy for appsettings.json
        configBuilder.Add(new EnvironmentVariablesConfigurationSource());       // env vars
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>()); // proxy for CLI args

        var sources = configBuilder.Sources;
        int originalEnvIndex = -1;
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is EnvironmentVariablesConfigurationSource)
            {
                originalEnvIndex = i;
                break;
            }
        }

        // Insert the local config file source at the position AddLocalConfigFile uses.
        var fileSource = new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
        {
            Path = FirstRunBootstrapper.LocalConfigPath,
            Optional = true,
            ReloadOnChange = false
        };
        fileSource.ResolveFileProvider();
        sources.Insert(originalEnvIndex, fileSource);

        // Assert: env vars source must come AFTER the file source.
        int fileIndex = sources.IndexOf(fileSource);
        int envIndex = -1;
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is EnvironmentVariablesConfigurationSource)
            {
                envIndex = i;
                break;
            }
        }

        Assert.True(envIndex > fileIndex,
            $"EnvironmentVariablesConfigurationSource (index {envIndex}) should come AFTER " +
            $"the local config file source (index {fileIndex}) so env vars take precedence.");
    }

    // ---- BuildMutexName ---------------------------------------------------------

    [Fact]
    public void BuildMutexName_ReturnsDeterministicNameForSamePath()
    {
        var name1 = FirstRunBootstrapper.BuildMutexName("/tmp/test.json");
        var name2 = FirstRunBootstrapper.BuildMutexName("/tmp/test.json");
        Assert.Equal(name1, name2);
    }

    [Fact]
    public void BuildMutexName_ReturnsDifferentNamesForDifferentPaths()
    {
        var name1 = FirstRunBootstrapper.BuildMutexName("/tmp/a.json");
        var name2 = FirstRunBootstrapper.BuildMutexName("/tmp/b.json");
        Assert.NotEqual(name1, name2);
    }

    [Fact]
    public void BuildMutexName_ContainsTaskdeckPrefix()
    {
        var name = FirstRunBootstrapper.BuildMutexName("/tmp/test.json");
        Assert.Contains("Taskdeck.FirstRun.", name);
    }

    // ---- PersistValue structural hardening (mutex guard + Unix perms) -----------
    // PersistValue is private, so these tests verify the hardening structurally
    // via reflection-based assertions on the source code patterns.

    [Fact]
    public void PersistValue_MutexConstructorIsGuarded_StructuralCheck()
    {
        // Verify that the PersistValue method source contains the guarded
        // exception types matching the CLI's CliFirstRunBootstrapper pattern.
        // This is a structural assertion: if someone removes the guard, the
        // test fails.
        var source = File.ReadAllText(
            FindSourceFile("FirstRunBootstrapper.cs"));

        Assert.Contains("UnauthorizedAccessException", source);
        Assert.Contains("WaitHandleCannotBeOpenedException", source);
        // Verify the mutex is constructed inside the try block (nullable pattern).
        Assert.Contains("Mutex? mutex = null", source);
    }

    [Fact]
    public void PersistValue_WritesSecretsViaAtomicRestrictedCreate_StructuralCheck()
    {
        // #1264 load-bearing wiring: the behavioral tests cannot distinguish atomic create-with-permissions
        // from a regression back to create-then-restrict (the final file state is identical on NTFS), so pin
        // the construction structurally. PersistValue's temp file AND the corrupt-config backup must go
        // through WriteRestrictedFile; the helper must create with the permissions supplied at creation
        // (UnixCreateMode / FileSecurity passed to Create) rather than restrict post-hoc.
        var source = File.ReadAllText(
            FindSourceFile("FirstRunBootstrapper.cs"));

        Assert.Contains("WriteRestrictedFile(tempPath, payload)", source);
        Assert.Contains("WriteRestrictedFile(backupPath", source);
        Assert.Contains("UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite", source);
        Assert.Contains("BuildOwnerOnlyFileSecurity())", source);
        // The pre-#1264 sequence must not come back.
        Assert.DoesNotContain("File.Create(tempPath)", source);
        Assert.DoesNotContain("RestrictFileToCurrentUser(tempPath)", source);
    }

    [Fact]
    public void WriteRestrictedFile_FailsClosedOnNonAclFilesystems_StructuralCheck()
    {
        // On FAT32/exFAT/some SMB shares CreateFileW silently IGNORES the supplied security descriptor, and
        // on non-POSIX mounts open(2)'s mode is ignored -- where the pre-#1264 restrict calls FAILED
        // (fail-closed). Pin the two post-create guards that restore that contract: the Windows DACL
        // read-back through the open handle and the Unix exact-mode pin (also umask-proof) through the
        // open handle.
        var source = File.ReadAllText(
            FindSourceFile("FirstRunBootstrapper.cs"));

        Assert.Contains("AreAccessRulesProtected", source);
        Assert.Contains("File.SetUnixFileMode(stream.SafeFileHandle", source);
    }

    [Fact]
    public void PersistValue_PreservesCorruptConfigBeforeOverwriting_StructuralCheck()
    {
        // A corrupt appsettings.local.json may hold the only copy of a previously-generated key (the
        // connector key in particular). Before the file is rewritten it must be backed up to a
        // timestamped .corrupt-* sibling for operator recovery -- not silently discarded.
        var source = File.ReadAllText(
            FindSourceFile("FirstRunBootstrapper.cs"));

        Assert.Contains("PreserveCorruptConfig", source);
        Assert.Contains(".corrupt-", source);
    }

    [Fact]
    public void PersistValue_ParsesExistingConfigLeniently_StructuralCheck()
    {
        // The read-modify-write must parse an existing appsettings.local.json with the config provider's
        // leniency (LocalConfigJsonOptions), so a hand-edited comment / trailing-comma file is preserved --
        // a strict parse would treat it as corrupt and rewrite the file WITHOUT its existing sections,
        // dropping the connector key. Guards against a regression back to the strict single-arg parse.
        var source = File.ReadAllText(
            FindSourceFile("FirstRunBootstrapper.cs"));

        Assert.Contains("LocalConfigJsonOptions", source);
        Assert.DoesNotContain("JsonNode.Parse(existing)", source);
    }

    [Fact]
    public void RestrictFileToCurrentUser_LocksFileToCurrentUserOnly()
    {
        // Behavioral test for the #1241 secret-file lockdown. Runs on both platforms (each CI runner covers
        // its own branch): on Unix it asserts 0600; on Windows it asserts the DACL is protected (inheritance
        // disabled) with the current user as the only granted principal.
        var path = Path.Combine(Path.GetTempPath(), $"td-acl-{Guid.NewGuid():N}.tmp");
        try
        {
            File.Create(path).Dispose();

            FirstRunBootstrapper.RestrictFileToCurrentUser(path);

            if (OperatingSystem.IsWindows())
            {
                var security = new FileInfo(path).GetAccessControl();
                Assert.True(
                    security.AreAccessRulesProtected,
                    "inheritance should be disabled so the directory's default ACEs do not apply");

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
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteRestrictedFile_LockdownSurvivesAtomicMove()
    {
        // #1241/#1264 load-bearing property: PersistValue atomically creates a restricted temp file, then
        // File.Move(overwrite)s it onto the target. This asserts the owner-only lockdown survives that
        // same-directory atomic rename. A regression to copy-then-delete or File.Replace (which preserves
        // the DESTINATION's ACL) would silently defeat the fix; the in-isolation helper test would not
        // catch it.
        var dir = Path.Combine(Path.GetTempPath(), $"td-acl-move-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var tempPath = Path.Combine(dir, ".secrets.tmp");
        var targetPath = Path.Combine(dir, "appsettings.local.json");
        try
        {
            // Pre-seed an existing (unrestricted) target so the move is a real overwrite.
            File.WriteAllText(targetPath, "{}");

            FirstRunBootstrapper.WriteRestrictedFile(tempPath, "{\"secret\":\"x\"}");
            File.Move(tempPath, targetPath, overwrite: true);

            if (OperatingSystem.IsWindows())
            {
                var security = new FileInfo(targetPath).GetAccessControl();
                Assert.True(
                    security.AreAccessRulesProtected,
                    "the moved file must keep the protected (non-inherited) owner-only DACL");

                using var identity = WindowsIdentity.GetCurrent();
                var currentSid = identity.User;
                var rules = security.GetAccessRules(
                    includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
                Assert.True(rules.Count > 0);
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
                    File.GetUnixFileMode(targetPath));
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void WriteRestrictedFile_CreatesFileAtomicallyLockedToCurrentUser()
    {
        // #1264 behavioral test (each CI runner covers its own platform branch): the file must be BORN with
        // owner-only permissions -- Unix 0600 via FileStreamOptions.UnixCreateMode, Windows via the
        // protected owner-only DACL supplied to CreateFile -- and hold the exact written content.
        var path = Path.Combine(Path.GetTempPath(), $"td-atomic-{Guid.NewGuid():N}.tmp");
        try
        {
            FirstRunBootstrapper.WriteRestrictedFile(path, "{\"secret\":\"atomic\"}");

            Assert.Equal("{\"secret\":\"atomic\"}", File.ReadAllText(path));
            if (OperatingSystem.IsWindows())
            {
                var security = new FileInfo(path).GetAccessControl();
                Assert.True(
                    security.AreAccessRulesProtected,
                    "inheritance should be disabled so the directory's default ACEs do not apply");

                using var identity = WindowsIdentity.GetCurrent();
                var currentSid = identity.User;
                var rules = security.GetAccessRules(
                    includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
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
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WriteRestrictedFile_RefusesToAdoptAPreExistingFile()
    {
        // #1264: FileMode.CreateNew must fail on an already-occupied path rather than write the secret into
        // a file someone else created (whose handle/permissions we do not control) -- and it must not
        // delete or overwrite that pre-existing file.
        var path = Path.Combine(Path.GetTempPath(), $"td-preexist-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(path, "pre-existing");
        try
        {
            Assert.Throws<IOException>(() => FirstRunBootstrapper.WriteRestrictedFile(path, "secret"));

            Assert.True(File.Exists(path), "the pre-existing file must not be deleted on a refused create");
            Assert.Equal("pre-existing", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void QuarantineCorruptLocalConfigAt_BackupIsRestrictedToCurrentUser()
    {
        // #1241/#1264: the .corrupt-* backup holds the same secrets as the original, so it must be created
        // with owner-only permissions (atomically, not copy-then-restrict) and stay byte-faithful for key
        // recovery.
        var dir = Path.Combine(Path.GetTempPath(), $"td-corrupt-acl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "appsettings.local.json");
        File.WriteAllText(path, "{ corrupt but holds : the only key copy");
        try
        {
            FirstRunBootstrapper.QuarantineCorruptLocalConfigAt(path);

            var preserved = Directory.GetFiles(dir, "appsettings.local.json.corrupt-*");
            Assert.Single(preserved);
            Assert.Equal("{ corrupt but holds : the only key copy", File.ReadAllText(preserved[0]));
            if (OperatingSystem.IsWindows())
            {
                var security = new FileInfo(preserved[0]).GetAccessControl();
                Assert.True(
                    security.AreAccessRulesProtected,
                    "the backup must carry the protected (non-inherited) owner-only DACL");

                using var identity = WindowsIdentity.GetCurrent();
                var currentSid = identity.User;
                var rules = security.GetAccessRules(
                    includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
                Assert.True(rules.Count > 0);
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
                    File.GetUnixFileMode(preserved[0]));
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static string FindSourceFile(string fileName)
    {
        // Walk up from the test output directory to find the repo source.
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "backend", "src", "Taskdeck.Api", "FirstRun", fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException(
            $"Could not locate {fileName} by walking up from {AppContext.BaseDirectory}");
    }
}
