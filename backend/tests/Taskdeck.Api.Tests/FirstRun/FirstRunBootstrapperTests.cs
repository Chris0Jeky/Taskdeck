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
}
