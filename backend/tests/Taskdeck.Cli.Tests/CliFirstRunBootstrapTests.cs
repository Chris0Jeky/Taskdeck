using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Connectors;
using Taskdeck.Cli;
using Taskdeck.Infrastructure;
using Xunit;

namespace Taskdeck.Cli.Tests;

/// <summary>
/// Covers issue #1131 AC1: the CLI must be self-sufficient on a fresh machine.
/// Before this fix, <c>taskdeck boards list</c> crashed at startup because
/// <c>AddInfrastructure</c> fail-fasts when <c>Connectors:EncryptionKey</c> is
/// missing and the CLI never provisioned one.
/// </summary>
public class CliFirstRunBootstrapTests
{
    // ----- End-to-end smoke test (strongest evidence) -----------------------

    [Fact]
    public async Task BoardsList_FreshMachine_BootstrapsKeyAndReturnsEmptyArray()
    {
        // No Connectors:EncryptionKey in the environment and no pre-existing
        // appsettings.local.json -- a genuinely clean machine.
        await using var harness = new CliTestHarness("cli-fresh-bootstrap", provisionEncryptionKey: false);

        var result = await harness.RunAsync("boards list --json");

        // It must NOT crash: it should bootstrap the key and return valid JSON.
        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(0);

        // The bootstrap persisted a key next to the data directory.
        var localConfig = Path.Combine(harness.DataDirectory, "appsettings.local.json");
        File.Exists(localConfig).Should().BeTrue("the CLI should persist the generated key");

        var json = await File.ReadAllTextAsync(localConfig);
        using var cfg = JsonDocument.Parse(json);
        var key = cfg.RootElement.GetProperty("Connectors").GetProperty("EncryptionKey").GetString();
        key.Should().NotBeNullOrWhiteSpace();
        Convert.FromBase64String(key!).Length.Should().Be(32, "the key must be a 256-bit AES key");
    }

    [Fact]
    public async Task BoardsList_FreshMachine_SecondRunReusesPersistedKey()
    {
        await using var harness = new CliTestHarness("cli-fresh-reuse", provisionEncryptionKey: false);
        var localConfig = Path.Combine(harness.DataDirectory, "appsettings.local.json");

        var first = await harness.RunAsync("boards list --json");
        first.ExitCode.Should().Be(0, first.StdErr);
        var firstKey = ReadPersistedKey(localConfig);

        var second = await harness.RunAsync("boards list --json");
        second.ExitCode.Should().Be(0, second.StdErr);
        var secondKey = ReadPersistedKey(localConfig);

        // A regenerated key on every run would make previously-encrypted connector
        // credentials undecryptable, so the key must be stable across invocations.
        secondKey.Should().Be(firstKey);
    }

    // ----- Unit tests for the bootstrapper ----------------------------------

    [Fact]
    public void EnsureConnectorEncryptionKey_WhenMissing_ProvisionsValidKeyAndPersistsIt()
    {
        using var temp = new TempDataDir();
        var configuration = BuildConfiguration(temp.DatabasePath);
        configuration["Connectors:EncryptionKey"].Should().BeNullOrWhiteSpace();

        CliFirstRunBootstrapper.EnsureConnectorEncryptionKey(configuration);

        var key = configuration["Connectors:EncryptionKey"];
        key.Should().NotBeNullOrWhiteSpace();
        Convert.FromBase64String(key!).Length.Should().Be(32);

        var localConfig = Path.Combine(temp.Directory, "appsettings.local.json");
        File.Exists(localConfig).Should().BeTrue();
        ReadPersistedKey(localConfig).Should().Be(key);
    }

    [Fact]
    public void EnsureConnectorEncryptionKey_WhenAlreadyConfigured_IsNoOp()
    {
        using var temp = new TempDataDir();
        const string existingKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
        var configuration = BuildConfiguration(temp.DatabasePath, existingKey);

        CliFirstRunBootstrapper.EnsureConnectorEncryptionKey(configuration);

        // The configured key is preserved and no file is written.
        configuration["Connectors:EncryptionKey"].Should().Be(existingKey);
        File.Exists(Path.Combine(temp.Directory, "appsettings.local.json")).Should().BeFalse();
    }

    [Fact]
    public void EnsureConnectorEncryptionKey_SecondCall_ReusesPersistedKey()
    {
        using var temp = new TempDataDir();

        var firstConfig = BuildConfiguration(temp.DatabasePath);
        CliFirstRunBootstrapper.EnsureConnectorEncryptionKey(firstConfig);
        var firstKey = firstConfig["Connectors:EncryptionKey"];

        // A fresh configuration (new process) with the file already on disk.
        var secondConfig = BuildConfiguration(temp.DatabasePath);
        CliFirstRunBootstrapper.EnsureConnectorEncryptionKey(secondConfig);
        var secondKey = secondConfig["Connectors:EncryptionKey"];

        secondKey.Should().Be(firstKey);
    }

    [Fact]
    public void EnsureConnectorEncryptionKey_PreservesOtherKeysInLocalConfigFile()
    {
        using var temp = new TempDataDir();
        var localConfig = Path.Combine(temp.Directory, "appsettings.local.json");
        // Pre-existing file with unrelated content the bootstrap must not destroy.
        File.WriteAllText(localConfig, "{\"Existing\":{\"Flag\":\"keep-me\"}}");

        var configuration = BuildConfiguration(temp.DatabasePath);
        CliFirstRunBootstrapper.EnsureConnectorEncryptionKey(configuration);

        using var cfg = JsonDocument.Parse(File.ReadAllText(localConfig));
        cfg.RootElement.GetProperty("Existing").GetProperty("Flag").GetString().Should().Be("keep-me");
        cfg.RootElement.GetProperty("Connectors").GetProperty("EncryptionKey").GetString()
            .Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("{ this is not valid json")]                       // corrupt JSON
    [InlineData("[1, 2, 3]")]                                       // valid JSON but not an object
    [InlineData("{\"Connectors\":{\"EncryptionKey\":12345}}")]     // wrong-type key value
    [InlineData("{\"Connectors\":{\"EncryptionKey\":\"\"}}")]      // empty key value
    public void EnsureConnectorEncryptionKey_WithMalformedLocalConfig_RegeneratesValidKeyWithoutThrowing(
        string malformedContent)
    {
        using var temp = new TempDataDir();
        var localConfig = Path.Combine(temp.Directory, "appsettings.local.json");
        File.WriteAllText(localConfig, malformedContent);

        var configuration = BuildConfiguration(temp.DatabasePath);

        // Must not throw: the fail-safe exists to prevent a startup crash.
        var act = () => CliFirstRunBootstrapper.EnsureConnectorEncryptionKey(configuration);
        act.Should().NotThrow();

        var key = configuration["Connectors:EncryptionKey"];
        key.Should().NotBeNullOrWhiteSpace();
        Convert.FromBase64String(key!).Length.Should().Be(32);

        // The file is overwritten with a valid persisted key.
        ReadPersistedKey(localConfig).Should().Be(key);
    }

    [Fact]
    public void GenerateKey_ProducesBase64EncodedTwoFiftySixBitKey()
    {
        var key = CliFirstRunBootstrapper.GenerateKey();

        key.Should().NotBeNullOrWhiteSpace();
        Convert.FromBase64String(key).Length.Should().Be(32);
        // Two generated keys must differ (cryptographically random).
        key.Should().NotBe(CliFirstRunBootstrapper.GenerateKey());
    }

    // ----- Integration test: AddInfrastructure succeeds after bootstrap -----

    [Fact]
    public void AddInfrastructure_SucceedsAfterBootstrap_OnCleanConfig()
    {
        using var temp = new TempDataDir();
        var configuration = BuildConfiguration(temp.DatabasePath);

        // Sanity: without the key AddInfrastructure must fail-fast (the bug).
        var noKeyServices = new ServiceCollection();
        noKeyServices.AddLogging();
        var act = () => noKeyServices.AddInfrastructure(configuration);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Connectors:EncryptionKey is not configured*");

        // After bootstrap, AddInfrastructure succeeds and the encryption service works.
        CliFirstRunBootstrapper.EnsureConnectorEncryptionKey(configuration);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        var encryptionService = provider.GetRequiredService<ICredentialEncryptionService>();
        encryptionService.Should().NotBeNull();

        // Round-trip proves the bootstrapped key is a usable AES-256 key.
        var cipher = encryptionService.Encrypt("secret");
        encryptionService.Decrypt(cipher).Should().Be("secret");
    }

    // ----- Helpers ----------------------------------------------------------

    private static ConfigurationManager BuildConfiguration(string databasePath, string? encryptionKey = null)
    {
        var configuration = new ConfigurationManager();
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Data Source={databasePath}"
        };
        if (encryptionKey is not null)
        {
            values["Connectors:EncryptionKey"] = encryptionKey;
        }

        configuration.AddInMemoryCollection(values);
        return configuration;
    }

    private static string? ReadPersistedKey(string localConfigPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(localConfigPath));
        return doc.RootElement.GetProperty("Connectors").GetProperty("EncryptionKey").GetString();
    }

    private sealed class TempDataDir : IDisposable
    {
        public string Directory { get; }
        public string DatabasePath { get; }

        public TempDataDir()
        {
            Directory = Path.Combine(Path.GetTempPath(), $"cli-bootstrap-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(Directory);
            DatabasePath = Path.Combine(Directory, "taskdeck.db");
        }

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Directory))
                {
                    System.IO.Directory.Delete(Directory, recursive: true);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
