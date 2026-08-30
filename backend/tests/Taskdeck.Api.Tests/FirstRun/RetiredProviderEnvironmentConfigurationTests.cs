using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.FirstRun;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Api.Tests.FirstRun;

/// <summary>
/// #2233: the packaged desktop start must survive retired provider configuration inherited from a
/// Windows profile, while retired configuration written into Taskdeck's own files keeps failing
/// closed. Every case here drives the real environment-variables provider through a per-test
/// random prefix, so the assertions never depend on (or disturb) the developer's own profile.
/// </summary>
public class RetiredProviderEnvironmentConfigurationTests
{
    private const string SyntheticRetiredValue = "synthetic-retired-gemini-value-2233";

    [Theory]
    [InlineData("Llm:Gemini:ApiKey", SyntheticRetiredValue, true)]
    [InlineData("llm:gemini:BaseUrl", "https://example.invalid", true)]
    [InlineData("Llm:Gemini:Model", "gemini-1.5", true)]
    [InlineData("Llm:Gemini", SyntheticRetiredValue, true)]
    [InlineData("Llm:Provider", "Gemini", true)]
    [InlineData("Llm:Provider", " gemini ", true)]
    [InlineData("TaskdeckMigration:RetiredLlmProviderConfigurationPresent", "true", true)]
    [InlineData("Llm:Provider", "OpenAI", false)]
    [InlineData("Llm:Provider", "Mock", false)]
    [InlineData("Llm:OpenAi:Model", "gpt-5.6-luna", false)]
    [InlineData("Llm:GeminiLike:ApiKey", SyntheticRetiredValue, false)]
    [InlineData("Llm:EnableLiveProviders", "true", false)]
    [InlineData("TaskdeckMigration:RetiredLlmProviderConfigurationPresent", "false", false)]
    public void IsRetiredEntry_MatchesOnlyRetiredProviderConfiguration(
        string key,
        string value,
        bool expected)
    {
        Assert.Equal(expected, RetiredProviderEnvironmentConfiguration.IsRetiredEntry(key, value));
    }

    [Fact]
    public void RemoveRetiredEntries_DropsRetiredKeysAndRecordsTheirNamesOnly()
    {
        var notice = new RetiredLlmProviderConfigurationNotice();
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Llm:Provider"] = "Gemini",
            ["Llm:Gemini:ApiKey"] = SyntheticRetiredValue,
            ["Llm:OpenAi:Model"] = "gpt-5.6-luna",
            ["Llm:EnableLiveProviders"] = "true"
        };

        var removed = RetiredProviderEnvironmentConfiguration.RemoveRetiredEntries(data, notice);

        Assert.Equal(new[] { "Llm:Gemini:ApiKey", "Llm:Provider" }, removed);
        Assert.Equal(new[] { "Llm:Gemini:ApiKey", "Llm:Provider" }, notice.IgnoredKeys);
        Assert.True(notice.IgnoredEnvironmentConfiguration);
        Assert.Equal(
            new[] { "Llm:EnableLiveProviders", "Llm:OpenAi:Model" },
            data.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.DoesNotContain(SyntheticRetiredValue, string.Join('\n', notice.IgnoredKeys));
    }

    [Fact]
    public void RemoveRetiredEntries_LeavesACleanEnvironmentUntouchedAndTheNoticeEmpty()
    {
        var notice = new RetiredLlmProviderConfigurationNotice();
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Llm:Provider"] = "Mock",
            ["Llm:OpenAi:Model"] = "gpt-5.6-luna"
        };

        var removed = RetiredProviderEnvironmentConfiguration.RemoveRetiredEntries(data, notice);

        Assert.Empty(removed);
        Assert.False(notice.IgnoredEnvironmentConfiguration);
        Assert.Equal(2, data.Count);
    }

    [Fact]
    public void RemoveRetiredEntries_CollapsesRepeatedRecordingsAcrossConfigurationReloads()
    {
        var notice = new RetiredLlmProviderConfigurationNotice();

        for (var reload = 0; reload < 3; reload++)
        {
            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Llm:Gemini:ApiKey"] = SyntheticRetiredValue
            };
            RetiredProviderEnvironmentConfiguration.RemoveRetiredEntries(data, notice);
        }

        Assert.Equal(new[] { "Llm:Gemini:ApiKey" }, notice.IgnoredKeys);
    }

    [Fact]
    public void IgnoreInheritedRetiredProviderConfiguration_DropsEnvironmentKeysButKeepsFileConfiguration()
    {
        var prefix = NewPrefix();
        var notice = new RetiredLlmProviderConfigurationNotice();
        var settingsPath = WriteSettingsFile(new Dictionary<string, object?>
        {
            ["Llm"] = new Dictionary<string, object?>
            {
                ["Provider"] = "Mock",
                ["Gemini"] = new Dictionary<string, string?> { ["ApiKey"] = SyntheticRetiredValue }
            }
        });

        var configuration = WithEnvironment(
            new Dictionary<string, string?>
            {
                [$"{prefix}Llm__Provider"] = "Gemini",
                [$"{prefix}Llm__Gemini__ApiKey"] = SyntheticRetiredValue,
                [$"{prefix}Llm__OpenAi__Model"] = "gpt-5.6-luna"
            },
            () =>
            {
                var builder = new ConfigurationBuilder();
                builder.AddJsonFile(settingsPath, optional: false, reloadOnChange: false);
                builder.AddEnvironmentVariables(prefix);
                RetiredProviderEnvironmentConfiguration.IgnoreInheritedRetiredProviderConfiguration(
                    builder,
                    notice);
                return builder.Build();
            });

        // The environment's retired selector and child are gone; the file's Gemini section — the
        // operator's own explicit choice — survives untouched, and non-retired environment keys
        // keep their higher precedence over the file.
        Assert.Equal("Mock", configuration["Llm:Provider"]);
        Assert.Equal(SyntheticRetiredValue, configuration["Llm:Gemini:ApiKey"]);
        Assert.Equal("gpt-5.6-luna", configuration["Llm:OpenAi:Model"]);
        Assert.Equal(new[] { "Llm:Gemini:ApiKey", "Llm:Provider" }, notice.IgnoredKeys);
    }

    [Theory]
    [InlineData("Gemini")]
    [InlineData(null)]
    public void AddLlmProviders_StartsOnTheDefaultProvider_WhenRetiredConfigurationComesFromTheEnvironment(
        string? environmentSelector)
    {
        var prefix = NewPrefix();
        var notice = new RetiredLlmProviderConfigurationNotice();
        var variables = new Dictionary<string, string?>
        {
            [$"{prefix}Llm__Gemini__ApiKey"] = SyntheticRetiredValue,
            [$"{prefix}Llm__Gemini__Model"] = "gemini-1.5",
            [$"{prefix}Llm__Gemini__BaseUrl"] = "https://generativelanguage.example.invalid"
        };
        if (environmentSelector is not null)
        {
            variables[$"{prefix}Llm__Provider"] = environmentSelector;
        }

        var configuration = WithEnvironment(variables, () =>
        {
            var builder = new ConfigurationBuilder();
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"] = "Mock"
            });
            builder.AddEnvironmentVariables(prefix);
            RetiredProviderEnvironmentConfiguration.IgnoreInheritedRetiredProviderConfiguration(
                builder,
                notice);
            return builder.Build();
        });

        var services = BuildServices();
        var exception = Record.Exception(() => services.AddLlmProviders(configuration));

        Assert.Null(exception);
        Assert.True(notice.IgnoredEnvironmentConfiguration);
        Assert.Null(configuration["Llm:Gemini:ApiKey"]);
        var decision = LlmProviderSelectionPolicy.Evaluate(
            configuration.GetSection("Llm").Get<LlmProviderSettings>() ?? new LlmProviderSettings(),
            "Production");
        Assert.Equal(LlmProviderKind.Mock, decision.ProviderKind);
    }

    [Fact]
    public void AddLlmProviders_StillFailsClosed_WhenRetiredConfigurationComesFromASettingsFile()
    {
        var prefix = NewPrefix();
        var notice = new RetiredLlmProviderConfigurationNotice();
        var settingsPath = WriteSettingsFile(new Dictionary<string, object?>
        {
            ["Llm"] = new Dictionary<string, object?>
            {
                ["Gemini"] = new Dictionary<string, string?> { ["ApiKey"] = SyntheticRetiredValue }
            }
        });

        var configuration = WithEnvironment(
            new Dictionary<string, string?> { [$"{prefix}Llm__OpenAi__Model"] = "gpt-5.6-luna" },
            () =>
            {
                var builder = new ConfigurationBuilder();
                builder.AddJsonFile(settingsPath, optional: false, reloadOnChange: false);
                builder.AddEnvironmentVariables(prefix);
                RetiredProviderEnvironmentConfiguration.IgnoreInheritedRetiredProviderConfiguration(
                    builder,
                    notice);
                return builder.Build();
            });

        var services = BuildServices();
        var act = () => services.AddLlmProviders(configuration);

        var exception = Assert.Throws<RetiredLlmProviderConfigurationException>(act);
        Assert.Equal(RetiredLlmProviderConfigurationReason.SettingsSection, exception.Reason);
        Assert.False(notice.IgnoredEnvironmentConfiguration);
    }

    [Fact]
    public void AddLlmProviders_Starts_WhenTheEnvironmentOnlyCarriesAmbientOpenAiPins()
    {
        var prefix = NewPrefix();
        var notice = new RetiredLlmProviderConfigurationNotice();

        var configuration = WithEnvironment(
            new Dictionary<string, string?>
            {
                [$"{prefix}Llm__OpenAi__Model"] = "stale-pinned-model",
                [$"{prefix}Llm__OpenAi__ApiKey"] = "synthetic-openai-value-2233"
            },
            () =>
            {
                var builder = new ConfigurationBuilder();
                builder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:Provider"] = "Mock"
                });
                builder.AddEnvironmentVariables(prefix);
                RetiredProviderEnvironmentConfiguration.IgnoreInheritedRetiredProviderConfiguration(
                    builder,
                    notice);
                return builder.Build();
            });

        var services = BuildServices();
        var exception = Record.Exception(() => services.AddLlmProviders(configuration));

        Assert.Null(exception);
        Assert.False(notice.IgnoredEnvironmentConfiguration);
        Assert.Equal("stale-pinned-model", configuration["Llm:OpenAi:Model"]);
    }

    private static IServiceCollection BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    private static string NewPrefix() => $"TASKDECK_TEST_{Guid.NewGuid():N}_";

    private static IConfigurationRoot WithEnvironment(
        IReadOnlyDictionary<string, string?> variables,
        Func<IConfigurationRoot> build)
    {
        try
        {
            foreach (var (name, value) in variables)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            return build();
        }
        finally
        {
            foreach (var name in variables.Keys)
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }
    }

    private static string WriteSettingsFile(IReadOnlyDictionary<string, object?> settings)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"taskdeck-2233-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "appsettings.local.json");
        File.WriteAllText(path, JsonSerializer.Serialize(settings));
        return path;
    }
}
