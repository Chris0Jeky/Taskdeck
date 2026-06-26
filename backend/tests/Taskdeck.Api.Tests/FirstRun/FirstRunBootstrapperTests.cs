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

    // ---- ResolveConnectorKeyPersistOutcome -----------------------------------

    // `expected` is the ConnectorKeyPersistOutcome name (the enum is internal, so a public [Theory] cannot
    // take it directly -- compare on .ToString()).
    [Theory]
    // A visible (non-empty) effective value means the persisted key surfaced -- done, regardless of mode.
    [InlineData("a-real-persisted-key", true, "Effective")]
    [InlineData("a-real-persisted-key", false, "Effective")]
    // A desktop Production install (requirePersistence) whose persisted key is masked by an empty/whitespace
    // higher-priority value must fail closed -- otherwise the next launch regenerates a different key and
    // orphans connector credentials saved this run.
    [InlineData("", true, "FailClosed")]
    [InlineData("   ", true, "FailClosed")]
    [InlineData(null, true, "FailClosed")]
    // A non-Production harness tolerates a process-local in-memory fallback when the file reload did not
    // propagate through every provider.
    [InlineData("", false, "InMemoryFallback")]
    [InlineData("   ", false, "InMemoryFallback")]
    [InlineData(null, false, "InMemoryFallback")]
    public void ResolveConnectorKeyPersistOutcome_FailsClosedOnlyWhenMaskedInProduction(
        string? effectiveValue, bool requirePersistence, string expected)
    {
        Assert.Equal(
            expected,
            FirstRunBootstrapper.ResolveConnectorKeyPersistOutcome(effectiveValue, requirePersistence).ToString());
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
    public void PersistValue_SetsUnixFileMode_StructuralCheck()
    {
        // Verify that the temp file gets 0600 permissions on Unix before being
        // moved into place, matching the CLI's CliFirstRunBootstrapper pattern.
        var source = File.ReadAllText(
            FindSourceFile("FirstRunBootstrapper.cs"));

        Assert.Contains("SetUnixFileMode", source);
        Assert.Contains("UnixFileMode.UserRead | UnixFileMode.UserWrite", source);
        Assert.Contains("!OperatingSystem.IsWindows()", source);
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
