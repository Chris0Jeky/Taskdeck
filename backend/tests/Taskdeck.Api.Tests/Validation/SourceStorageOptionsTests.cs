using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Infrastructure;
using Xunit;

namespace Taskdeck.Api.Tests.Validation;

public class SourceStorageOptionsTests
{
    [Theory]
    [InlineData(nameof(BlobStorageSettings.MaximumUploadBytes), "0")]
    [InlineData(nameof(BlobStorageSettings.MaximumUploadBytes), "-1")]
    [InlineData(nameof(BlobStorageSettings.OwnerQuotaBytes), "0")]
    [InlineData(nameof(BlobStorageSettings.OwnerQuotaBytes), "-1")]
    [InlineData(nameof(BlobStorageSettings.ModalityQuotaBytes), "0")]
    [InlineData(nameof(BlobStorageSettings.ModalityQuotaBytes), "-1")]
    [InlineData(nameof(BlobStorageSettings.MaximumReferencesPerOwner), "0")]
    [InlineData(nameof(BlobStorageSettings.MaximumReferencesPerOwner), "-1")]
    public void SharedInfrastructureRejectsNonpositiveLimits(string property, string value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"SourceStorage:{property}"] = value,
            ["Connectors:EncryptionKey"] = ApiTestHarness.TestEncryptionKey
        }).Build();
        using var provider = new ServiceCollection().AddInfrastructure(config).BuildServiceProvider();
        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<BlobStorageSettings>());
        Assert.Contains(property, error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedInfrastructureUsesTheValidatedSettingsInstance(bool largeLimits)
    {
        var values = largeLimits ? new Dictionary<string, string?>
        {
            ["SourceStorage:MaximumUploadBytes"] = long.MaxValue.ToString(),
            ["SourceStorage:OwnerQuotaBytes"] = long.MaxValue.ToString(),
            ["SourceStorage:ModalityQuotaBytes"] = long.MaxValue.ToString(),
            ["SourceStorage:MaximumReferencesPerOwner"] = int.MaxValue.ToString()
        } : [];
        values["Connectors:EncryptionKey"] = ApiTestHarness.TestEncryptionKey;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        using var provider = new ServiceCollection().AddInfrastructure(config).BuildServiceProvider();
        var settings = provider.GetRequiredService<BlobStorageSettings>();
        Assert.Same(provider.GetRequiredService<IOptions<BlobStorageSettings>>().Value, settings);
        Assert.Equal(largeLimits ? long.MaxValue : new BlobStorageSettings().OwnerQuotaBytes, settings.OwnerQuotaBytes);
    }

    [Fact]
    public void ApiRefusesToStartBeforeAnUploadWithInvalidOwnerQuota()
    {
        using var baseFactory = new TestWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseSetting("SourceStorage:OwnerQuotaBytes", "0"));
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(nameof(BlobStorageSettings.OwnerQuotaBytes), error.ToString());
    }
}
