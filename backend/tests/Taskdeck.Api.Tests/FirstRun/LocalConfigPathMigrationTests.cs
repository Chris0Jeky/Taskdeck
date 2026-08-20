using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Api.FirstRun;
using Xunit;

namespace Taskdeck.Api.Tests.FirstRun;

public sealed class LocalConfigPathMigrationTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void ResolveLocalConfigPath_UsesDurablePathOnlyForDesktopProduction(
        bool isProduction,
        bool isHeadless,
        bool expectDurable)
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "exe");
        var appData = Path.Combine(temp.Path, "appdata", "Taskdeck");

        var actual = FirstRunBootstrapper.ResolveLocalConfigPath(
            isProduction,
            isHeadless,
            executable,
            appData);

        var expectedDirectory = expectDurable ? appData : executable;
        Assert.Equal(
            Path.GetFullPath(Path.Combine(expectedDirectory, "appsettings.local.json")),
            actual);
        Assert.True(Path.IsPathRooted(actual));
    }

    [Fact]
    public void ResolveAppDataPath_HonoursExplicitAbsoluteOverride()
    {
        using var temp = new TempDirectory();
        var overridePath = Path.Combine(temp.Path, "isolated-localappdata");
        var knownFolder = Path.Combine(temp.Path, "known-folder");

        var actual = FirstRunBootstrapper.ResolveAppDataPath(overridePath, knownFolder);

        Assert.Equal(Path.Combine(overridePath, "Taskdeck"), actual);
    }

    [Fact]
    public void ImportLegacyLocalConfigIfNeeded_CopiesCompleteFileAndRetainsSource()
    {
        using var temp = new TempDirectory();
        var legacy = Path.Combine(temp.Path, "legacy", "appsettings.local.json");
        var durable = Path.Combine(temp.Path, "durable", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        const string payload = "{\n  \"Jwt\": { \"SecretKey\": \"jwt-value\" },\n" +
            "  \"Connectors\": { \"EncryptionKey\": \"connector-value\" },\n" +
            "  \"Custom\": { \"Keep\": \"whole-file\" }\n}";
        File.WriteAllText(legacy, payload);

        FirstRunBootstrapper.ImportLegacyLocalConfigIfNeeded(legacy, durable);

        Assert.Equal(payload, File.ReadAllText(legacy));
        Assert.Equal(payload, File.ReadAllText(durable));
        AssertOwnerOnly(legacy);
        AssertOwnerOnly(durable);
    }

    [Fact]
    public void ImportLegacyLocalConfigIfNeeded_DurableFileWinsWithoutMerge()
    {
        using var temp = new TempDirectory();
        var legacy = Path.Combine(temp.Path, "legacy", "appsettings.local.json");
        var durable = Path.Combine(temp.Path, "durable", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        Directory.CreateDirectory(Path.GetDirectoryName(durable)!);
        File.WriteAllText(legacy, "{\"LegacyOnly\":\"preserved\"}");
        File.WriteAllText(durable, "{\"DurableOnly\":\"authoritative\"}");

        FirstRunBootstrapper.ImportLegacyLocalConfigIfNeeded(legacy, durable);

        Assert.Equal("{\"DurableOnly\":\"authoritative\"}", File.ReadAllText(durable));
        Assert.DoesNotContain("LegacyOnly", File.ReadAllText(durable));
        Assert.Equal("{\"LegacyOnly\":\"preserved\"}", File.ReadAllText(legacy));
        AssertOwnerOnly(legacy);
        AssertOwnerOnly(durable);
    }

    [Fact]
    public async Task ImportLegacyLocalConfigIfNeeded_ConcurrentCallsConvergeOnCompleteFile()
    {
        using var temp = new TempDirectory();
        var legacy = Path.Combine(temp.Path, "legacy", "appsettings.local.json");
        var durable = Path.Combine(temp.Path, "durable", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        const string payload = "{\"Connectors\":{\"EncryptionKey\":\"one-key\"},\"Keep\":42}";
        File.WriteAllText(legacy, payload);

        await Task.WhenAll(
            Task.Run(() => FirstRunBootstrapper.ImportLegacyLocalConfigIfNeeded(legacy, durable)),
            Task.Run(() => FirstRunBootstrapper.ImportLegacyLocalConfigIfNeeded(legacy, durable)));

        Assert.Equal(payload, File.ReadAllText(durable));
        _ = new ConfigurationBuilder().AddJsonFile(durable).Build();
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(durable)!, "*.import.tmp"));
    }

    [Fact]
    public void ImportLegacyLocalConfigIfNeeded_ChangingSourceFailsBeforeTargetCreation()
    {
        using var temp = new TempDirectory();
        var legacy = Path.Combine(temp.Path, "legacy", "appsettings.local.json");
        var replacement = Path.Combine(temp.Path, "legacy", "replacement.json");
        var durable = Path.Combine(temp.Path, "durable", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, "{\"Connectors\":{\"EncryptionKey\":\"old\"}}");
        File.WriteAllText(replacement, "{\"Connectors\":{\"EncryptionKey\":\"new\"}}");

        var error = Assert.Throws<InvalidOperationException>(() =>
            FirstRunBootstrapper.ImportLegacyLocalConfigIfNeeded(
                legacy,
                durable,
                beforeRead: _ => File.Move(replacement, legacy, overwrite: true)));

        Assert.Contains("refusing", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(durable));
        Assert.True(File.Exists(legacy), "the recovery source must survive a failed replacement attempt");
    }

    [Fact]
    public void ImportLegacyLocalConfigIfNeeded_CorruptSourceFailsClosed()
    {
        using var temp = new TempDirectory();
        var legacy = Path.Combine(temp.Path, "legacy", "appsettings.local.json");
        var durable = Path.Combine(temp.Path, "durable", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, "{ not complete");

        Assert.Throws<InvalidOperationException>(() =>
            FirstRunBootstrapper.ImportLegacyLocalConfigIfNeeded(legacy, durable));
        Assert.True(File.Exists(legacy));
        Assert.False(File.Exists(durable));
    }

    [Fact]
    public void PrepareLocalConfigFile_CorruptDurableFileFailsClosedWithoutReplacement()
    {
        using var temp = new TempDirectory();
        var legacy = Path.Combine(temp.Path, "legacy", "appsettings.local.json");
        var durable = Path.Combine(temp.Path, "durable", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(durable)!);
        const string corruptPayload = "{ may contain an unrecoverable key";
        File.WriteAllText(durable, corruptPayload);

        var error = Assert.Throws<InvalidOperationException>(() =>
            FirstRunBootstrapper.PrepareLocalConfigFile(
                durable,
                legacy,
                requireOwnerOnly: true));

        Assert.Contains("not a complete provider-loadable JSON object", error.Message);
        Assert.Equal(corruptPayload, File.ReadAllText(durable));
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(durable)!,
            "appsettings.local.json.corrupt-*"));
    }

    [Fact]
    public void ImportLegacyLocalConfigIfNeeded_PermissionFailureStopsBeforeReadOrCopy()
    {
        using var temp = new TempDirectory();
        var legacy = Path.Combine(temp.Path, "legacy", "appsettings.local.json");
        var durable = Path.Combine(temp.Path, "durable", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, "{\"Connectors\":{\"EncryptionKey\":\"secret\"}}");

        Assert.Throws<InvalidOperationException>(() =>
            FirstRunBootstrapper.ImportLegacyLocalConfigIfNeeded(
                legacy,
                durable,
                restrictFile: _ => throw new IOException("simulated ACL failure")));
        Assert.False(File.Exists(durable));
    }

    [Fact]
    public void EnsureBootstrapSecrets_ExistingDatabaseWithoutKeyWritesNeitherSecret()
    {
        using var temp = new TempDirectory();
        var database = Path.Combine(temp.Path, "data", "taskdeck.db");
        var localConfig = Path.Combine(temp.Path, "data", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        File.WriteAllBytes(database, [0x54, 0x44]);
        var configuration = BuildConfiguration(database);

        var error = Assert.Throws<InvalidOperationException>(() =>
            FirstRunBootstrapper.EnsureBootstrapSecrets(
                configuration,
                NullLogger.Instance,
                localConfig,
                isProduction: true,
                isHeadless: false,
                resolveDatabaseToAppData: true,
                databaseAppDataPath: Path.GetDirectoryName(database),
                legacyDatabaseDirectory: Path.Combine(temp.Path, "legacy")));

        Assert.Contains("no supplied or persisted connector encryption key", error.Message);
        Assert.Null(configuration["Connectors:EncryptionKey"]);
        Assert.Null(configuration["Jwt:SecretKey"]);
        Assert.False(File.Exists(localConfig));
    }

    [Fact]
    public void EnsureBootstrapSecrets_LegacyAdjacentDatabaseCannotBeSilentlyAbandoned()
    {
        using var temp = new TempDirectory();
        var durableDirectory = Path.Combine(temp.Path, "appdata", "Taskdeck");
        var durableDatabase = Path.Combine(durableDirectory, "taskdeck.db");
        var localConfig = Path.Combine(durableDirectory, "appsettings.local.json");
        var legacyDirectory = Path.Combine(temp.Path, "release");
        Directory.CreateDirectory(legacyDirectory);
        Directory.CreateDirectory(durableDirectory);
        FirstRunBootstrapper.WriteRestrictedFile(
            localConfig,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                ConnectionStrings = new { DefaultConnection = $"Data Source={durableDatabase}" }
            }));
        File.WriteAllBytes(Path.Combine(legacyDirectory, "taskdeck.db"), [0x54, 0x44]);
        File.WriteAllBytes(Path.Combine(legacyDirectory, "taskdeck.db-wal"), [0x57, 0x41, 0x4C]);
        File.WriteAllBytes(Path.Combine(legacyDirectory, "taskdeck.db-shm"), [0x53, 0x48, 0x4D]);
        var configuration = BuildConfiguration(durableDatabase);

        var error = Assert.Throws<InvalidOperationException>(() =>
            FirstRunBootstrapper.EnsureBootstrapSecrets(
                configuration,
                NullLogger.Instance,
                localConfig,
                isProduction: true,
                isHeadless: false,
                resolveDatabaseToAppData: true,
                databaseAppDataPath: durableDirectory,
                legacyDatabaseDirectory: legacyDirectory));

        Assert.Contains("silently abandon v0.1 data", error.Message);
        Assert.False(File.Exists(durableDatabase));
        Assert.True(File.Exists(localConfig), "the migrated recovery evidence must remain intact");
        Assert.DoesNotContain("Connectors", File.ReadAllText(localConfig));
        Assert.DoesNotContain("Jwt", File.ReadAllText(localConfig));
        Assert.Null(configuration["Connectors:EncryptionKey"]);
        Assert.Null(configuration["Jwt:SecretKey"]);
    }

    [Fact]
    public void EnsureBootstrapSecrets_ExplicitAbsoluteDatabaseBypassesAdjacentLegacyProbe()
    {
        using var temp = new TempDirectory();
        var customDatabase = Path.Combine(temp.Path, "custom", "taskdeck.db");
        var durableDirectory = Path.Combine(temp.Path, "appdata", "Taskdeck");
        var localConfig = Path.Combine(durableDirectory, "appsettings.local.json");
        var legacyDirectory = Path.Combine(temp.Path, "release");
        Directory.CreateDirectory(legacyDirectory);
        File.WriteAllBytes(Path.Combine(legacyDirectory, "taskdeck.db"), [0x54, 0x44]);
        var configuration = BuildConfiguration(customDatabase);

        FirstRunBootstrapper.EnsureBootstrapSecrets(
            configuration,
            NullLogger.Instance,
            localConfig,
            isProduction: true,
            isHeadless: false,
            resolveDatabaseToAppData: true,
            databaseAppDataPath: durableDirectory,
            legacyDatabaseDirectory: legacyDirectory);

        Assert.False(string.IsNullOrWhiteSpace(configuration["Connectors:EncryptionKey"]));
        Assert.False(string.IsNullOrWhiteSpace(configuration["Jwt:SecretKey"]));
        Assert.False(File.Exists(customDatabase));
    }

    [Fact]
    public void EnsureBootstrapSecrets_MaskedPersistedKeySurvivesExistingDatabase()
    {
        using var temp = new TempDirectory();
        var database = Path.Combine(temp.Path, "data", "taskdeck.db");
        var localConfig = Path.Combine(temp.Path, "data", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        File.WriteAllBytes(database, [0x54, 0x44]);
        FirstRunBootstrapper.WriteRestrictedFile(
            localConfig,
            "{\"Connectors\":{\"EncryptionKey\":\"persisted-key\"}}");
        var configuration = BuildConfiguration(database, connectorKey: " ");

        FirstRunBootstrapper.EnsureBootstrapSecrets(
            configuration,
            NullLogger.Instance,
            localConfig,
            isProduction: true,
            isHeadless: false,
            resolveDatabaseToAppData: true,
            databaseAppDataPath: Path.GetDirectoryName(database),
            legacyDatabaseDirectory: Path.Combine(temp.Path, "legacy"));

        Assert.Equal("persisted-key", configuration["Connectors:EncryptionKey"]);
        Assert.False(string.IsNullOrWhiteSpace(configuration["Jwt:SecretKey"]));
        Assert.Equal(
            "persisted-key",
            new ConfigurationBuilder().AddJsonFile(localConfig).Build()["Connectors:EncryptionKey"]);
    }

    [Fact]
    public void EnsureBootstrapSecrets_FreshDatabaseGeneratesDurableIdentity()
    {
        using var temp = new TempDirectory();
        var dataDirectory = Path.Combine(temp.Path, "data");
        var database = Path.Combine(dataDirectory, "taskdeck.db");
        var localConfig = Path.Combine(dataDirectory, "appsettings.local.json");
        var configuration = BuildConfiguration(database);

        FirstRunBootstrapper.EnsureBootstrapSecrets(
            configuration,
            NullLogger.Instance,
            localConfig,
            isProduction: true,
            isHeadless: false,
            resolveDatabaseToAppData: true,
            databaseAppDataPath: dataDirectory,
            legacyDatabaseDirectory: Path.Combine(temp.Path, "legacy"));

        Assert.False(string.IsNullOrWhiteSpace(configuration["Connectors:EncryptionKey"]));
        Assert.False(string.IsNullOrWhiteSpace(configuration["Jwt:SecretKey"]));
        var persisted = new ConfigurationBuilder().AddJsonFile(localConfig).Build();
        Assert.Equal(configuration["Connectors:EncryptionKey"], persisted["Connectors:EncryptionKey"]);
        Assert.Equal(configuration["Jwt:SecretKey"], persisted["Jwt:SecretKey"]);
        AssertOwnerOnly(localConfig);
    }

    [Fact]
    public async Task EnsureConnectorEncryptionKey_ConcurrentCreatorsReuseOneWinner()
    {
        using var temp = new TempDirectory();
        var localConfig = Path.Combine(temp.Path, "data", "appsettings.local.json");
        var first = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var second = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var barrier = new Barrier(2);

        await Task.WhenAll(
            Task.Run(() => FirstRunBootstrapper.EnsureConnectorEncryptionKey(
                first,
                NullLogger.Instance,
                localConfig,
                requirePersistence: true,
                afterInitialAbsence: () => barrier.SignalAndWait(),
                secretFactory: () => "first-candidate")),
            Task.Run(() => FirstRunBootstrapper.EnsureConnectorEncryptionKey(
                second,
                NullLogger.Instance,
                localConfig,
                requirePersistence: true,
                afterInitialAbsence: () => barrier.SignalAndWait(),
                secretFactory: () => "second-candidate")));

        var persisted = new ConfigurationBuilder().AddJsonFile(localConfig).Build()["Connectors:EncryptionKey"];
        Assert.True(persisted is "first-candidate" or "second-candidate");
        Assert.Equal(persisted, first["Connectors:EncryptionKey"]);
        Assert.Equal(persisted, second["Connectors:EncryptionKey"]);
    }

    [Fact]
    public void EnsureDbPath_PersistsAndUsesExactSelectedConfigPath()
    {
        using var temp = new TempDirectory();
        var appData = Path.Combine(temp.Path, "appdata", "Taskdeck");
        var localConfig = Path.Combine(appData, "appsettings.local.json");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=taskdeck.db"
            })
            .Build();

        FirstRunBootstrapper.EnsureDbPath(
            configuration,
            NullLogger.Instance,
            localConfig,
            appData);

        var expected = $"Data Source={Path.Combine(appData, "taskdeck.db")}";
        Assert.Equal(expected, configuration.GetConnectionString("DefaultConnection"));
        Assert.Equal(
            expected,
            new ConfigurationBuilder().AddJsonFile(localConfig).Build()
                .GetConnectionString("DefaultConnection"));
    }

    [Fact]
    public void EnsureDbPath_PreservesInMemoryOverrideWithoutCreatingLocalConfig()
    {
        using var temp = new TempDirectory();
        var appData = Path.Combine(temp.Path, "appdata", "Taskdeck");
        var localConfig = Path.Combine(appData, "appsettings.local.json");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:"
            })
            .Build();

        FirstRunBootstrapper.EnsureDbPath(
            configuration,
            NullLogger.Instance,
            localConfig,
            appData);

        Assert.Equal("Data Source=:memory:", configuration.GetConnectionString("DefaultConnection"));
        Assert.False(File.Exists(localConfig));
    }

    [Fact]
    public void Program_ThreadsResolvedPathThroughWebAndBothMcpHosts()
    {
        var source = File.ReadAllText(FindProgramSource());

        Assert.Contains("mcpHttpLocalConfigPath = FirstRunBootstrapper.ResolveLocalConfigPath", source);
        Assert.Contains("mcpHttpBuilder.AddLocalConfigFile(mcpHttpLocalConfigPath)", source);
        Assert.Contains("mcpStdioLocalConfigPath ??= FirstRunBootstrapper.ResolveLocalConfigPath", source);
        Assert.Contains("config.AddJsonFile(mcpStdioLocalConfigPath, optional: true)", source);
        Assert.Contains("localConfigPath = FirstRunBootstrapper.ResolveLocalConfigPath", source);
        Assert.Contains("builder.AddLocalConfigFile(localConfigPath)", source);
        Assert.Contains("builder.RunFirstRunChecks(bootstrapLogger, localConfigPath)", source);
        Assert.Contains("builder.ValidateProductionSecrets(bootstrapLogger, localConfigPath)", source);
    }

    private static IConfigurationRoot BuildConfiguration(string databasePath, string? connectorKey = null)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={databasePath}",
                ["Connectors:EncryptionKey"] = connectorKey
            })
            .Build();

    private static void AssertOwnerOnly(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(path));
            return;
        }

        var security = new FileInfo(path).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        using var identity = WindowsIdentity.GetCurrent();
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
        Assert.True(rules.Count > 0);
        foreach (FileSystemAccessRule rule in rules)
        {
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
            Assert.Equal(identity.User, rule.IdentityReference);
        }
    }

    private static string FindProgramSource()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            var candidate = Path.Combine(directory, "backend", "src", "Taskdeck.Api", "Program.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new FileNotFoundException("Could not locate Taskdeck.Api/Program.cs from test output.");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"taskdeck-config-migration-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best-effort test cleanup */ }
        }
    }
}
