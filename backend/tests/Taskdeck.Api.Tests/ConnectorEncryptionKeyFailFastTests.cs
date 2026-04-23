using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Infrastructure;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ConnectorEncryptionKeyFailFastTests
{
    [Fact]
    public void AddInfrastructure_WithoutEncryptionKey_ShouldThrowInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddInfrastructure(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Connectors:EncryptionKey*not configured*");
    }

    [Fact]
    public void AddInfrastructure_WithEmptyEncryptionKey_ShouldThrowInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                ["Connectors:EncryptionKey"] = ""
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // null-coalescing throws on null, but empty string passes that check
        // and hits the AesCredentialEncryptionService constructor validation.
        var act = () => services.AddInfrastructure(config);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void AddInfrastructure_WithValidEncryptionKey_ShouldNotThrow()
    {
        // Generate a valid base64-encoded 256-bit key.
        var keyBytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes);
        var validKey = Convert.ToBase64String(keyBytes);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                ["Connectors:EncryptionKey"] = validKey
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddInfrastructure(config);

        act.Should().NotThrow();
    }
}
