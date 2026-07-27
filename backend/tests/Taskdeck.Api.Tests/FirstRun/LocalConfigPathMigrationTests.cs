using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Taskdeck.Api.FirstRun;
using Xunit;

namespace Taskdeck.Api.Tests.FirstRun;

public sealed class LocalConfigPathMigrationTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void ResolveLocalConfigPath_UsesDurablePathOnlyForNonHeadlessProduction(
        bool isProduction,
        bool isHeadless,
        bool expectDurable)
    {
        using var temp = new TempDirectory();
        var executableDirectory = Path.Combine(temp.Path, "publish");
        var appDataDirectory = Path.Combine(temp.Path, "app-data", "Taskdeck");

        var actual = FirstRunBootstrapper.ResolveLocalConfigPath(
            isProduction,
            isHeadless,
            executableDirectory,
            appDataDirectory);

        var expectedDirectory = expectDurable ? appDataDirectory : executableDirectory;
        Assert.Equal(Path.Combine(expectedDirectory, "appsettings.local.json"), actual);
    }

    [Fact]
    public void ImportLegacyLocalConfigIfNeeded_CopiesCompleteJsonAndRetainsSource()
    {
        using var temp = new TempDirectory();
        var legacyPath = Path.Combine(temp.Path, "publish", "appsettings.local.json");
        var durablePath = Path.Combine(temp.Path, "app-data", "Taskdeck", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        const string legacyJson = "{\n  \"Jwt\": { \"SecretKey\": \"legacy-jwt\" },\n  \"Connectors\": { \"EncryptionKey\": \"legacy-connector\" },\n  \"Custom\": { \"Keep\": true }\n}";
        File.WriteAllText(legacyPath, legacyJson);

        FirstRunBootstrapper.ImportLegacyLocalConfigIfNeeded(legacyPath, durablePath);

        Assert.True(File.Exists(legacyPath));
        Assert.Equal(legacyJson, File.ReadAllText(legacyPath));
        Assert.Equal(legacyJson, File.ReadAllText(durablePath));
    }

    [Fact]
    public void ImportLegacyLocalConfigIfNeeded_DurableFileWinsWithoutMergeOrOverwrite()
    {
        using var temp = new TempDirectory();
        var legacyPath = Path.Combine(temp.Path, "publish", "appsettings.local.json");
        var durablePath = Path.Combine(temp.Path, "app-data", "Taskdeck", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(durablePath)!);
        File.WriteAllText(legacyPath, "{\"LegacyOnly\":true}");
        File.WriteAllText(durablePath, "{\"DurableOnly\":true}");
        MakeReadableByOtherUsers(legacyPath);
        AssertReadableByOtherUsers(legacyPath);

        FirstRunBootstrapper.ImportLegacyLocalConfigIfNeeded(legacyPath, durablePath);

        Assert.Equal("{\"LegacyOnly\":true}", File.ReadAllText(legacyPath));
        Assert.Equal("{\"DurableOnly\":true}", File.ReadAllText(durablePath));
        AssertOwnerOnly(legacyPath);
    }

    [Fact]
    public async Task ImportLegacyLocalConfigIfNeeded_ConcurrentImportAcceptsOneCompleteWinner()
    {
        using var temp = new TempDirectory();
        var legacyPath = Path.Combine(temp.Path, "publish", "appsettings.local.json");
        var durablePath = Path.Combine(temp.Path, "app-data", "Taskdeck", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        var legacyJson = "{\"Jwt\":{\"SecretKey\":\"legacy-jwt\"},\"Connectors\":{\"EncryptionKey\":\"legacy-connector\"}}";
        File.WriteAllText(legacyPath, legacyJson);

        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(
            () => FirstRunBootstrapper.ImportLegacyLocalConfigIfNeeded(legacyPath, durablePath))));

        Assert.Equal(legacyJson, File.ReadAllText(durablePath));
        Assert.NotNull(JsonNode.Parse(File.ReadAllText(durablePath))?.AsObject());
        Assert.True(File.Exists(legacyPath));
        AssertOwnerOnly(durablePath);
    }

    [Fact]
    public void ImportLegacyLocalConfigIfNeeded_LooseSourceAndDestinationBecomeOwnerOnly()
    {
        using var temp = new TempDirectory();
        var legacyPath = Path.Combine(temp.Path, "publish", "appsettings.local.json");
        var durablePath = Path.Combine(temp.Path, "app-data", "Taskdeck", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, "{\"Connectors\":{\"EncryptionKey\":\"legacy\"}}");
        MakeReadableByOtherUsers(legacyPath);
        AssertReadableByOtherUsers(legacyPath);
        var observedOwnerOnlyBeforeRead = false;

        FirstRunBootstrapper.ImportLegacyLocalConfigIfNeeded(
            legacyPath,
            durablePath,
            beforeRead: securedPath =>
            {
                AssertOwnerOnly(securedPath);
                observedOwnerOnlyBeforeRead = true;
            });

        Assert.True(observedOwnerOnlyBeforeRead);
        AssertOwnerOnly(legacyPath);
        AssertOwnerOnly(durablePath);
    }

    [Fact]
    public void ImportLegacyLocalConfigIfNeeded_BeforeReadReplacementIsNotCopiedAndEndsOwnerOnly()
    {
        using var temp = new TempDirectory();
        var legacyPath = Path.Combine(temp.Path, "publish", "appsettings.local.json");
        var durablePath = Path.Combine(temp.Path, "app-data", "Taskdeck", "appsettings.local.json");
        var replacementPath = Path.Combine(temp.Path, "publish", "replacement.json");
        var displacedPath = Path.Combine(temp.Path, "publish", "displaced.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        const string original = "{\"Connectors\":{\"EncryptionKey\":\"original-key\"}}";
        const string replacement = "{\"Connectors\":{\"EncryptionKey\":\"replacement-key\"}}";
        File.WriteAllText(legacyPath, original);

        var error = Assert.Throws<InvalidOperationException>(() =>
            FirstRunBootstrapper.ImportLegacyLocalConfigIfNeeded(
                legacyPath,
                durablePath,
                beforeRead: observedPath =>
                {
                    File.WriteAllText(replacementPath, replacement);
                    MakeReadableByOtherUsers(replacementPath);
                    File.Move(observedPath, displacedPath);
                    File.Move(replacementPath, observedPath);
                }));

        Assert.Contains("changed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(durablePath));
        Assert.Equal(replacement, File.ReadAllText(legacyPath));
        AssertOwnerOnly(legacyPath);
        File.Delete(displacedPath);
    }

    [Fact]
    public void RemoveImportedDurableIfExact_ConcurrentReplacementSurvivesConditionalCleanup()
    {
        using var temp = new TempDirectory();
        var durablePath = Path.Combine(temp.Path, "appsettings.local.json");
        var stalePayload = "{\"Connectors\":{\"EncryptionKey\":\"stale\"}}"u8.ToArray();
        var winnerPayload = "{\"Connectors\":{\"EncryptionKey\":\"winner\"}}"u8.ToArray();
        File.WriteAllBytes(durablePath, stalePayload);

        var removed = FirstRunBootstrapper.RemoveImportedDurableIfExact(
            durablePath,
            stalePayload,
            capturedPath => File.WriteAllBytes(capturedPath, winnerPayload));

        Assert.True(removed);
        Assert.Equal(winnerPayload, File.ReadAllBytes(durablePath));
        AssertOwnerOnly(durablePath);
        Assert.Empty(Directory.GetFiles(temp.Path, "appsettings.local.json.stale-import-*"));
    }

    [Fact]
    public void ImportLegacyLocalConfigIfNeeded_LateSourceReplacementRemainsOwnerOnlyAndStaleImportIsRemoved()
    {
        using var temp = new TempDirectory();
        var legacyPath = Path.Combine(temp.Path, "publish", "appsettings.local.json");
        var displacedPath = Path.Combine(temp.Path, "publish", "displaced.json");
        var durablePath = Path.Combine(temp.Path, "app-data", "Taskdeck", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        const string original = "{\"Connectors\":{\"EncryptionKey\":\"original\"}}";
        const string replacement = "{\"Connectors\":{\"EncryptionKey\":\"late-winner\"}}";
        File.WriteAllText(legacyPath, original);
        var legacyRestrictionCount = 0;

        var error = Assert.Throws<InvalidOperationException>(() =>
            FirstRunBootstrapper.ImportLegacyLocalConfigIfNeeded(
                legacyPath,
                durablePath,
                restrictFile: candidate =>
                {
                    FirstRunBootstrapper.RestrictFileToCurrentUser(candidate);
                    if (!Path.GetFullPath(candidate).Equals(
                            Path.GetFullPath(legacyPath),
                            OperatingSystem.IsWindows()
                                ? StringComparison.OrdinalIgnoreCase
                                : StringComparison.Ordinal)
                        || Interlocked.Increment(ref legacyRestrictionCount) != 3)
                    {
                        return;
                    }

                    File.Move(legacyPath, displacedPath);
                    File.WriteAllText(legacyPath, replacement);
                    MakeReadableByOtherUsers(legacyPath);
                }));

        Assert.Contains("stale durable import was removed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(replacement, File.ReadAllText(legacyPath));
        AssertOwnerOnly(legacyPath);
        Assert.False(File.Exists(durablePath));
    }

    [Fact]
    public async Task EnsureConnectorEncryptionKey_ConcurrentCreatorsUseOnePersistedWinner()
    {
        using var temp = new TempDirectory();
        var localConfigPath = Path.Combine(temp.Path, "config", "appsettings.local.json");
        var firstConfiguration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var secondConfiguration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var bothObservedAbsent = new Barrier(2);
        var generatedCount = 0;

        Task RunContender(IConfiguration configuration) => Task.Run(() =>
            FirstRunBootstrapper.EnsureConnectorEncryptionKey(
                configuration,
                new RecordingLogger(),
                localConfigPath,
                requirePersistence: true,
                afterInitialAbsence: () => Assert.True(
                    bothObservedAbsent.SignalAndWait(TimeSpan.FromSeconds(10))),
                lockTimeout: TimeSpan.FromSeconds(10),
                secretFactory: () => $"candidate-{Interlocked.Increment(ref generatedCount)}"));

        await Task.WhenAll(RunContender(firstConfiguration), RunContender(secondConfiguration));

        var persisted = JsonNode.Parse(File.ReadAllText(localConfigPath))!
            ["Connectors"]!["EncryptionKey"]!.GetValue<string>();
        Assert.Equal(1, generatedCount);
        Assert.Equal(persisted, firstConfiguration["Connectors:EncryptionKey"]);
        Assert.Equal(persisted, secondConfiguration["Connectors:EncryptionKey"]);
        AssertOwnerOnly(localConfigPath);
    }

    [Fact]
    public async Task EnsureConnectorEncryptionKey_LockTimeoutFailsClosedWithoutWriting()
    {
        using var temp = new TempDirectory();
        var localConfigPath = Path.Combine(temp.Path, "config", "appsettings.local.json");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var holderReady = new ManualResetEventSlim();
        using var releaseHolder = new ManualResetEventSlim();
        var generatedCount = 0;
        var holder = Task.Run(() =>
        {
            using var mutex = new Mutex(false, FirstRunBootstrapper.BuildMutexName(localConfigPath));
            mutex.WaitOne();
            try
            {
                holderReady.Set();
                Assert.True(releaseHolder.Wait(TimeSpan.FromSeconds(10)));
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        });

        Assert.True(holderReady.Wait(TimeSpan.FromSeconds(10)));
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => Task.Run(() =>
                FirstRunBootstrapper.EnsureConnectorEncryptionKey(
                    configuration,
                    new RecordingLogger(),
                    localConfigPath,
                    requirePersistence: true,
                    lockTimeout: TimeSpan.Zero,
                    secretFactory: () => $"candidate-{Interlocked.Increment(ref generatedCount)}")));
        }
        finally
        {
            releaseHolder.Set();
            await holder;
        }

        Assert.Equal(0, generatedCount);
        Assert.False(File.Exists(localConfigPath));
        Assert.True(string.IsNullOrWhiteSpace(configuration["Connectors:EncryptionKey"]));
    }

    [Fact]
    public async Task EnsureJwtSecret_ConcurrentCreatorsUseOnePersistedWinner()
    {
        using var temp = new TempDirectory();
        var localConfigPath = Path.Combine(temp.Path, "config", "appsettings.local.json");
        var firstConfiguration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var secondConfiguration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var bothObservedAbsent = new Barrier(2);
        var generatedCount = 0;

        Task RunContender(IConfiguration configuration) => Task.Run(() =>
            FirstRunBootstrapper.EnsureJwtSecret(
                configuration,
                new RecordingLogger(),
                localConfigPath,
                requirePersistence: true,
                afterInitialAbsence: () => Assert.True(
                    bothObservedAbsent.SignalAndWait(TimeSpan.FromSeconds(10))),
                lockTimeout: TimeSpan.FromSeconds(10),
                secretFactory: () => $"jwt-candidate-{Interlocked.Increment(ref generatedCount)}"));

        await Task.WhenAll(RunContender(firstConfiguration), RunContender(secondConfiguration));

        var persisted = JsonNode.Parse(File.ReadAllText(localConfigPath))!
            ["Jwt"]!["SecretKey"]!.GetValue<string>();
        Assert.Equal(1, generatedCount);
        Assert.Equal(persisted, firstConfiguration["Jwt:SecretKey"]);
        Assert.Equal(persisted, secondConfiguration["Jwt:SecretKey"]);
        AssertOwnerOnly(localConfigPath);
    }

    [Fact]
    public void EnsureJwtSecret_FlattenedPlaceholderIsUpdatedInPlaceWithoutProviderCollision()
    {
        using var temp = new TempDirectory();
        var localConfigPath = Path.Combine(temp.Path, "appsettings.local.json");
        File.WriteAllText(
            localConfigPath,
            "{\"jwt:secretkey\":\"\",\"Jwt\":\"operator-metadata\",\"Other\":\"keep\"}");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(localConfigPath, optional: false, reloadOnChange: false)
            .Build();

        FirstRunBootstrapper.EnsureJwtSecret(
            configuration,
            new RecordingLogger(),
            localConfigPath,
            requirePersistence: true,
            secretFactory: () => "persisted-jwt");

        var root = JsonNode.Parse(File.ReadAllText(localConfigPath))!.AsObject();
        var flattened = root.First(
            pair => pair.Key.Equals("Jwt:SecretKey", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("persisted-jwt", flattened.Value!.GetValue<string>());
        Assert.Equal("operator-metadata", root["Jwt"]!.GetValue<string>());
        Assert.Equal("keep", root["Other"]!.GetValue<string>());
        Assert.Equal(
            "persisted-jwt",
            new ConfigurationBuilder()
                .AddJsonFile(localConfigPath, optional: false, reloadOnChange: false)
                .Build()["Jwt:SecretKey"]);
        AssertOwnerOnly(localConfigPath);
    }

    [Fact]
    public void EnsureDbPath_FlattenedConnectionStringIsUpdatedInPlaceWithoutProviderCollision()
    {
        using var temp = new TempDirectory();
        var localConfigPath = Path.Combine(temp.Path, "appsettings.local.json");
        var appDataPath = Path.Combine(temp.Path, "app-data", "Taskdeck");
        File.WriteAllText(
            localConfigPath,
            "{\"connectionstrings:defaultconnection\":\"Data Source=legacy.db\"," +
            "\"ConnectionStrings\":\"operator-metadata\",\"Other\":\"keep\"}");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(localConfigPath, optional: false, reloadOnChange: false)
            .Build();

        FirstRunBootstrapper.EnsureDbPath(
            configuration,
            new RecordingLogger(),
            localConfigPath,
            appDataPath);

        var expected = $"Data Source={Path.Combine(appDataPath, "legacy.db")}";
        var root = JsonNode.Parse(File.ReadAllText(localConfigPath))!.AsObject();
        var flattened = root.First(
            pair => pair.Key.Equals("ConnectionStrings:DefaultConnection", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expected, flattened.Value!.GetValue<string>());
        Assert.Equal("operator-metadata", root["ConnectionStrings"]!.GetValue<string>());
        Assert.Equal("keep", root["Other"]!.GetValue<string>());
        Assert.Equal(
            expected,
            new ConfigurationBuilder()
                .AddJsonFile(localConfigPath, optional: false, reloadOnChange: false)
                .Build().GetConnectionString("DefaultConnection"));
        AssertOwnerOnly(localConfigPath);
    }

    [Fact]
    public void EnsureDbPath_HigherPriorityRelativeConnectionUsesResolvedPathWhenSqliteOpens()
    {
        using var temp = new TempDirectory();
        var localConfigPath = Path.Combine(temp.Path, "appsettings.local.json");
        var appDataPath = Path.Combine(temp.Path, "app-data", "Taskdeck");
        var sourceDatabasePath = Path.Combine(temp.Path, "source", "relative-source.db");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceDatabasePath)!);
        byte[] sourceMarker = [0x6e, 0x6f, 0x74, 0x2d, 0x73, 0x71, 0x6c, 0x69, 0x74, 0x65];
        File.WriteAllBytes(sourceDatabasePath, sourceMarker);
        File.WriteAllText(localConfigPath, "{}");
        var relativeSource = Path.GetRelativePath(Directory.GetCurrentDirectory(), sourceDatabasePath);
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(localConfigPath, optional: false, reloadOnChange: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={relativeSource}"
            })
            .Build();

        FirstRunBootstrapper.EnsureDbPath(
            configuration,
            new RecordingLogger(),
            localConfigPath,
            appDataPath);

        var expectedDatabasePath = Path.Combine(appDataPath, Path.GetFileName(sourceDatabasePath));
        var effectiveConnectionString = configuration.GetConnectionString("DefaultConnection");
        var effectiveDataSource = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(
            effectiveConnectionString).DataSource;
        Assert.Equal(expectedDatabasePath, effectiveDataSource);

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(effectiveConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE BootstrapPathProbe (Id INTEGER PRIMARY KEY);";
            command.ExecuteNonQuery();
        }

        Assert.True(File.Exists(expectedDatabasePath));
        Assert.Equal(sourceMarker, File.ReadAllBytes(sourceDatabasePath));
    }

    [Fact]
    public void EnsureJwtSecret_RequiredPersistenceFailureDoesNotInstallTransientSecret()
    {
        using var temp = new TempDirectory();
        var blockingParent = Path.Combine(temp.Path, "not-a-directory");
        File.WriteAllText(blockingParent, "block directory creation");
        var localConfigPath = Path.Combine(blockingParent, "appsettings.local.json");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var error = Assert.Throws<InvalidOperationException>(() =>
            FirstRunBootstrapper.EnsureJwtSecret(
                configuration,
                new RecordingLogger(),
                localConfigPath,
                requirePersistence: true,
                secretFactory: () => "must-not-be-installed"));

        Assert.Contains("persist", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("block directory creation", File.ReadAllText(blockingParent));
        Assert.True(string.IsNullOrWhiteSpace(configuration["Jwt:SecretKey"]));
    }

    [Fact]
    public async Task ApiAndCliProcesses_UseOneDurableLockAndConvergeOnThePersistedKey()
    {
        using var temp = new TempDirectory();
        var databasePath = Path.Combine(temp.Path, "taskdeck.db");
        var localConfigPath = Path.Combine(temp.Path, "appsettings.local.json");
        var configuration = BuildConfiguration(databasePath);
        using var holderReady = new ManualResetEventSlim();
        using var releaseHolder = new ManualResetEventSlim();
        using var apiObservedAbsent = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            using var mutex = new Mutex(false, FirstRunBootstrapper.BuildMutexName(localConfigPath));
            mutex.WaitOne();
            try
            {
                holderReady.Set();
                Assert.True(releaseHolder.Wait(TimeSpan.FromSeconds(15)));
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        });
        Task? apiTask = null;
        Process? cli = null;
        Task<string>? stdoutTask = null;
        var cliStarted = false;
        using var cliContended = new ManualResetEventSlim();
        var stderrLines = new ConcurrentQueue<string>();
        try
        {
            Assert.True(holderReady.Wait(TimeSpan.FromSeconds(10)));
            apiTask = Task.Run(() => FirstRunBootstrapper.EnsureConnectorEncryptionKey(
                configuration,
                new RecordingLogger(),
                localConfigPath,
                requirePersistence: true,
                afterInitialAbsence: apiObservedAbsent.Set,
                lockTimeout: TimeSpan.FromSeconds(15),
                secretFactory: () => "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="));
            Assert.True(apiObservedAbsent.Wait(TimeSpan.FromSeconds(10)));

            cli = StartCliProcess(databasePath);
            cli.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is null)
                {
                    return;
                }

                stderrLines.Enqueue(eventArgs.Data);
                if (eventArgs.Data.Contains(
                        "Waiting for another Taskdeck process to finish durable local-config initialization.",
                        StringComparison.Ordinal))
                {
                    cliContended.Set();
                }
            };
            cliStarted = cli.Start();
            Assert.True(cliStarted);
            cli.BeginErrorReadLine();
            stdoutTask = cli.StandardOutput.ReadToEndAsync();

            var observedContention = cliContended.Wait(TimeSpan.FromSeconds(10));
            var earlyError = string.Join(Environment.NewLine, stderrLines);
            Assert.True(
                observedContention,
                $"the CLI subprocess never reported reaching the contended shared lock " +
                $"(exited={cli.HasExited}): {earlyError}");
            Assert.False(File.Exists(localConfigPath),
                "the CLI subprocess must contend on the same lock held for the API path");
            if (cli.HasExited)
            {
                Assert.Fail(
                    $"the CLI subprocess exited instead of waiting on the shared durable lock " +
                    $"(exit {cli.ExitCode}): {earlyError}");
            }

            releaseHolder.Set();
            await holder;
            await apiTask;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await cli.WaitForExitAsync(timeout.Token);
            cli.WaitForExit();
            var stdout = await stdoutTask;
            var stderr = string.Join(Environment.NewLine, stderrLines);
            Assert.Equal(0, cli.ExitCode);
            Assert.Contains("[", stdout);

            var persisted = JsonNode.Parse(File.ReadAllText(localConfigPath))!
                ["Connectors"]!["EncryptionKey"]!.GetValue<string>();
            Assert.Equal(persisted, configuration["Connectors:EncryptionKey"]);
            Assert.DoesNotContain("transient", stderr, StringComparison.OrdinalIgnoreCase);
            AssertOwnerOnly(localConfigPath);
        }
        finally
        {
            releaseHolder.Set();

            try { await holder.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* cleanup observes best-effort */ }
            if (apiTask is not null)
            {
                try { await apiTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* observed by main path */ }
            }

            if (cliStarted && cli is not null)
            {
                try
                {
                    if (!cli.HasExited)
                    {
                        cli.Kill(entireProcessTree: true);
                    }

                    await cli.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch { /* best-effort subprocess cleanup */ }
            }

            if (stdoutTask is not null)
            {
                try { await stdoutTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* drain best-effort */ }
            }

            cli?.Dispose();
        }
    }

    [Fact]
    public void EnsureBootstrapSecrets_ExistingDatabaseWithoutKeyThrowsBeforeGeneratingEitherSecret()
    {
        using var temp = new TempDirectory();
        var databasePath = Path.Combine(temp.Path, "existing.db");
        var localConfigPath = Path.Combine(temp.Path, "config", "appsettings.local.json");
        File.WriteAllBytes(databasePath, [0x53, 0x51, 0x4c]);
        var configuration = BuildConfiguration(databasePath);

        var error = Assert.Throws<InvalidOperationException>(() =>
            FirstRunBootstrapper.EnsureBootstrapSecrets(
                configuration,
                new RecordingLogger(),
                localConfigPath,
                isProduction: true,
                isHeadless: false,
                resolveDatabaseToAppData: true));

        Assert.Contains("existing database", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(localConfigPath));
        Assert.True(string.IsNullOrWhiteSpace(configuration["Connectors:EncryptionKey"]));
        Assert.True(string.IsNullOrWhiteSpace(configuration["Jwt:SecretKey"]));
    }

    [Fact]
    public void EnsureBootstrapSecrets_RecoversMaskedPersistedKeyBeforeGeneratingJwt()
    {
        using var temp = new TempDirectory();
        var databasePath = Path.Combine(temp.Path, "existing.db");
        var localConfigPath = Path.Combine(temp.Path, "appsettings.local.json");
        File.WriteAllBytes(databasePath, [0x53, 0x51, 0x4c]);
        File.WriteAllText(
            localConfigPath,
            "{\"Connectors\":{\"EncryptionKey\":\"persisted-key\"}}");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(localConfigPath, optional: false, reloadOnChange: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={databasePath}",
                ["Connectors:EncryptionKey"] = ""
            })
            .Build();

        FirstRunBootstrapper.EnsureBootstrapSecrets(
            configuration,
            new RecordingLogger(),
            localConfigPath,
            isProduction: true,
            isHeadless: false,
            resolveDatabaseToAppData: true);

        Assert.Equal("persisted-key", configuration["Connectors:EncryptionKey"]);
        var root = JsonNode.Parse(File.ReadAllText(localConfigPath))!.AsObject();
        Assert.Equal("persisted-key", root["Connectors"]!["EncryptionKey"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(root["Jwt"]!["SecretKey"]!.GetValue<string>()));
    }

    [Fact]
    public void EnsureBootstrapSecrets_ExplicitKeyKeepsPrecedenceOverPersistedKey()
    {
        using var temp = new TempDirectory();
        var databasePath = Path.Combine(temp.Path, "existing.db");
        var localConfigPath = Path.Combine(temp.Path, "appsettings.local.json");
        File.WriteAllBytes(databasePath, [0x53, 0x51, 0x4c]);
        File.WriteAllText(
            localConfigPath,
            "{\"Connectors\":{\"EncryptionKey\":\"persisted-key\"},\"Jwt\":{\"SecretKey\":\"persisted-jwt\"}}");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(localConfigPath, optional: false, reloadOnChange: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={databasePath}",
                ["Connectors:EncryptionKey"] = "explicit-key"
            })
            .Build();

        FirstRunBootstrapper.EnsureBootstrapSecrets(
            configuration,
            new RecordingLogger(),
            localConfigPath,
            isProduction: true,
            isHeadless: false,
            resolveDatabaseToAppData: true);

        Assert.Equal("explicit-key", configuration["Connectors:EncryptionKey"]);
        Assert.Contains("persisted-key", File.ReadAllText(localConfigPath));
        Assert.DoesNotContain("explicit-key", File.ReadAllText(localConfigPath));
    }

    [Fact]
    public void EnsureBootstrapSecrets_FreshDatabaseGeneratesBothPersistedSecrets()
    {
        using var temp = new TempDirectory();
        var databasePath = Path.Combine(temp.Path, "fresh.db");
        var localConfigPath = Path.Combine(temp.Path, "config", "appsettings.local.json");
        var configuration = BuildConfiguration(databasePath);

        FirstRunBootstrapper.EnsureBootstrapSecrets(
            configuration,
            new RecordingLogger(),
            localConfigPath,
            isProduction: true,
            isHeadless: false,
            resolveDatabaseToAppData: true);

        var root = JsonNode.Parse(File.ReadAllText(localConfigPath))!.AsObject();
        Assert.False(string.IsNullOrWhiteSpace(root["Connectors"]!["EncryptionKey"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(root["Jwt"]!["SecretKey"]!.GetValue<string>()));
        Assert.False(File.Exists(databasePath));
    }

    [Fact]
    public void CorruptDurableConfig_WithExistingDatabaseIsPreservedAndCannotGenerateReplacementSecrets()
    {
        using var temp = new TempDirectory();
        var databasePath = Path.Combine(temp.Path, "existing.db");
        var legacyPath = Path.Combine(temp.Path, "publish", "appsettings.local.json");
        var durablePath = Path.Combine(temp.Path, "app-data", "Taskdeck", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(durablePath)!);
        File.WriteAllBytes(databasePath, [0x53, 0x51, 0x4c]);
        File.WriteAllText(legacyPath, "{\"Connectors\":{\"EncryptionKey\":\"legacy-key\"}}");
        File.WriteAllText(durablePath, "{ corrupt but may contain the durable key");

        FirstRunBootstrapper.PrepareLocalConfigFile(durablePath, legacyPath);
        var configuration = BuildConfiguration(databasePath);

        Assert.Throws<InvalidOperationException>(() =>
            FirstRunBootstrapper.EnsureBootstrapSecrets(
                configuration,
                new RecordingLogger(),
                durablePath,
                isProduction: true,
                isHeadless: false,
                resolveDatabaseToAppData: true));

        Assert.False(File.Exists(durablePath));
        var preserved = Directory.GetFiles(Path.GetDirectoryName(durablePath)!, "appsettings.local.json.corrupt-*");
        Assert.Single(preserved);
        Assert.Contains("durable key", File.ReadAllText(preserved[0]));
        Assert.DoesNotContain("legacy-key", Directory.GetFiles(Path.GetDirectoryName(durablePath)!).Select(File.ReadAllText));
        Assert.True(File.Exists(legacyPath));

        var secondLaunchError = Assert.Throws<InvalidOperationException>(() =>
            FirstRunBootstrapper.PrepareLocalConfigFile(durablePath, legacyPath));
        Assert.Contains("recovery", secondLaunchError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(durablePath));
        Assert.Equal("{\"Connectors\":{\"EncryptionKey\":\"legacy-key\"}}", File.ReadAllText(legacyPath));
        Assert.Single(Directory.GetFiles(
            Path.GetDirectoryName(durablePath)!,
            "appsettings.local.json.corrupt-*"));
    }

    [Fact]
    public void PrepareLocalConfigFile_BackupFailurePreservesOriginalAndFailsBeforeSecretGeneration()
    {
        using var temp = new TempDirectory();
        var durablePath = Path.Combine(temp.Path, "app-data", "Taskdeck", "appsettings.local.json");
        var legacyPath = Path.Combine(temp.Path, "publish", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(durablePath)!);
        var corruptBytes = "{ corrupt durable key material"u8.ToArray();
        File.WriteAllBytes(durablePath, corruptBytes);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var error = Assert.Throws<InvalidOperationException>(() =>
            FirstRunBootstrapper.PrepareLocalConfigFile(
                durablePath,
                legacyPath,
                requireOwnerOnly: true,
                restrictFile: candidate =>
                {
                    if (candidate.Contains(".corrupt-", StringComparison.Ordinal))
                    {
                        throw new IOException("injected backup owner-only verification failure");
                    }

                    FirstRunBootstrapper.RestrictFileToCurrentUser(candidate);
                }));

        Assert.Contains("restored", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(corruptBytes, File.ReadAllBytes(durablePath));
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(durablePath)!,
            "appsettings.local.json.corrupt-*"));
        Assert.True(string.IsNullOrWhiteSpace(configuration["Connectors:EncryptionKey"]));
        Assert.True(string.IsNullOrWhiteSpace(configuration["Jwt:SecretKey"]));
    }

    [Fact]
    public void PrepareLocalConfigFile_NonProductionValidAclFailureRemainsBestEffort()
    {
        using var temp = new TempDirectory();
        var durablePath = Path.Combine(temp.Path, "app-data", "Taskdeck", "appsettings.local.json");
        var legacyPath = Path.Combine(temp.Path, "publish", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(durablePath)!);
        const string durableJson = "{\"Connectors\":{\"EncryptionKey\":\"durable-key\"}}";
        File.WriteAllText(durablePath, durableJson);

        FirstRunBootstrapper.PrepareLocalConfigFile(
            durablePath,
            legacyPath,
            requireOwnerOnly: false,
            restrictFile: _ => throw new IOException("injected best-effort ACL failure"));

        Assert.Equal(durableJson, File.ReadAllText(durablePath));
    }

    [Fact]
    public void PrepareLocalConfigFile_NonProductionCorruptAclFailureStillPreservesExactBackup()
    {
        using var temp = new TempDirectory();
        var durablePath = Path.Combine(temp.Path, "app-data", "Taskdeck", "appsettings.local.json");
        var legacyPath = Path.Combine(temp.Path, "publish", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(durablePath)!);
        var corruptBytes = "{ corrupt durable key material"u8.ToArray();
        File.WriteAllBytes(durablePath, corruptBytes);

        FirstRunBootstrapper.PrepareLocalConfigFile(
            durablePath,
            legacyPath,
            requireOwnerOnly: false,
            restrictFile: _ => throw new IOException("injected best-effort ACL failure"));

        Assert.False(File.Exists(durablePath));
        var backups = Directory.GetFiles(
            Path.GetDirectoryName(durablePath)!,
            "appsettings.local.json.corrupt-*");
        Assert.Single(backups);
        Assert.Equal(corruptBytes, File.ReadAllBytes(backups[0]));
    }

    [Fact]
    public void PrepareLocalConfigFile_ProductionExistingDurableAclFailureStopsBeforeLoad()
    {
        using var temp = new TempDirectory();
        var durablePath = Path.Combine(temp.Path, "app-data", "Taskdeck", "appsettings.local.json");
        var legacyPath = Path.Combine(temp.Path, "publish", "appsettings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(durablePath)!);
        const string durableJson = "{\"Connectors\":{\"EncryptionKey\":\"durable-key\"}}";
        File.WriteAllText(durablePath, durableJson);

        var error = Assert.Throws<InvalidOperationException>(() =>
            FirstRunBootstrapper.PrepareLocalConfigFile(
                durablePath,
                legacyPath,
                requireOwnerOnly: true,
                restrictFile: _ => throw new IOException("injected ACL failure")));

        Assert.Contains("owner-only", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(durableJson, File.ReadAllText(durablePath));
    }

    [Fact]
    public void Program_UsesResolvedLocalConfigPathForWebAndBothMcpTransports()
    {
        var source = File.ReadAllText(FindRepoFile("backend", "src", "Taskdeck.Api", "Program.cs"));

        Assert.Contains("var mcpHttpLocalConfigPath = FirstRunBootstrapper.ResolveLocalConfigPath(", source);
        Assert.Contains("mcpHttpBuilder.AddLocalConfigFile(mcpHttpLocalConfigPath)", source);
        Assert.Contains("Program.ResolveMcpStdioEnvironmentName(", source);
        Assert.Contains("mcpStdioLocalConfigPath ??= FirstRunBootstrapper.ResolveLocalConfigPath(", source);
        Assert.Contains("requireOwnerOnly: isProduction && !isHeadless", source);
        Assert.Contains("config.AddJsonFile(mcpStdioLocalConfigPath, optional: true)", source);
        Assert.Contains("var localConfigPath = FirstRunBootstrapper.ResolveLocalConfigPath(", source);
        Assert.Contains("builder.AddLocalConfigFile(localConfigPath)", source);
        Assert.DoesNotContain("FirstRunBootstrapper.LocalConfigPath", source);
        Assert.Equal(1, CountOccurrences(source, ".RunFirstRunChecks("));
    }

    [Theory]
    [InlineData("Production", "Development", "Development")]
    [InlineData("Development", "Production", "Production")]
    [InlineData("Development", null, "Development")]
    [InlineData("Staging", "", "Staging")]
    public void Program_McpStdioEnvironmentPreservesAspNetCoreEnvironmentParity(
        string hostEnvironment,
        string? aspNetCoreEnvironment,
        string expected)
    {
        Assert.Equal(
            expected,
            Program.ResolveMcpStdioEnvironmentName(hostEnvironment, aspNetCoreEnvironment));
    }

    [Fact]
    public void EnsureBootstrapSecrets_DiagnosticsNeverContainSecretValues()
    {
        using var temp = new TempDirectory();
        var databasePath = Path.Combine(temp.Path, "fresh.db");
        var localConfigPath = Path.Combine(temp.Path, "config", "appsettings.local.json");
        var configuration = BuildConfiguration(databasePath);
        var logger = new RecordingLogger();

        FirstRunBootstrapper.EnsureBootstrapSecrets(
            configuration,
            logger,
            localConfigPath,
            isProduction: true,
            isHeadless: false,
            resolveDatabaseToAppData: true);

        var persisted = JsonNode.Parse(File.ReadAllText(localConfigPath))!;
        var generatedConnectorKey = persisted["Connectors"]!["EncryptionKey"]!.GetValue<string>();
        var generatedJwt = persisted["Jwt"]!["SecretKey"]!.GetValue<string>();
        var diagnostics = string.Join(Environment.NewLine, logger.Messages);
        Assert.DoesNotContain(generatedConnectorKey, diagnostics);
        Assert.DoesNotContain(generatedJwt, diagnostics);
    }

    private static IConfigurationRoot BuildConfiguration(string databasePath)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={databasePath}"
            })
            .Build();

    private static Process StartCliProcess(string databasePath)
    {
        var cliDll = Path.Combine(AppContext.BaseDirectory, "Taskdeck.Cli.dll");
        Assert.True(File.Exists(cliDll), $"CLI test dependency was not built at {cliDll}");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(databasePath)!
        };
        startInfo.ArgumentList.Add(cliDll);
        startInfo.ArgumentList.Add("boards");
        startInfo.ArgumentList.Add("list");
        startInfo.ArgumentList.Add("--json");
        startInfo.Environment["TASKDECK_CONNECTION_STRING"] = $"Data Source={databasePath}";
        startInfo.Environment["ConnectionStrings__DefaultConnection"] = $"Data Source={databasePath}";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment.Remove("Connectors__EncryptionKey");
        startInfo.Environment.Remove("TASKDECK_CONNECTORS__ENCRYPTIONKEY");
        return new Process { StartInfo = startInfo };
    }

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

    private static void MakeReadableByOtherUsers(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            return;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new InvalidOperationException("Current user SID unavailable.");
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var security = new FileSecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            owner,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            everyone,
            FileSystemRights.Read,
            AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    private static void AssertReadableByOtherUsers(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(
                (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) != 0);
            return;
        }

        AssertWindowsReadableByOtherUsers(path);
    }

    [SupportedOSPlatform("windows")]
    private static void AssertWindowsReadableByOtherUsers(string path)
    {
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var rules = new FileInfo(path).GetAccessControl()
            .GetAccessRules(true, true, typeof(SecurityIdentifier));
        Assert.Contains(
            rules.Cast<FileSystemAccessRule>(),
            rule => rule.AccessControlType == AccessControlType.Allow
                && rule.IdentityReference.Equals(everyone)
                && (rule.FileSystemRights & FileSystemRights.Read) != 0);
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

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
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
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
