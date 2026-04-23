using FluentAssertions;
using Taskdeck.Application.Connectors;
using Taskdeck.Domain.Connectors;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Connectors;

public class ConnectorProviderRegistryTests
{
    [Fact]
    public void GetAll_ShouldReturnAllRegisteredProviders()
    {
        var providers = new IConnectorProvider[]
        {
            new FakeProvider("github", ConnectorType.GitHubIssueIntake, ConnectorDirection.Context),
            new FakeProvider("slack", ConnectorType.Custom, ConnectorDirection.Outbound),
        };

        var registry = new ConnectorProviderRegistry(providers);

        registry.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void GetById_ShouldReturnProvider_WhenExists()
    {
        var github = new FakeProvider("github", ConnectorType.GitHubIssueIntake, ConnectorDirection.Context);
        var registry = new ConnectorProviderRegistry(new[] { github });

        var result = registry.GetById("github");

        result.Should().NotBeNull();
        result!.ProviderId.Should().Be("github");
    }

    [Fact]
    public void GetById_ShouldBeCaseInsensitive()
    {
        var github = new FakeProvider("github", ConnectorType.GitHubIssueIntake, ConnectorDirection.Context);
        var registry = new ConnectorProviderRegistry(new[] { github });

        registry.GetById("GitHub").Should().NotBeNull();
        registry.GetById("GITHUB").Should().NotBeNull();
    }

    [Fact]
    public void GetById_ShouldReturnNull_WhenNotFound()
    {
        var registry = new ConnectorProviderRegistry(Array.Empty<IConnectorProvider>());

        registry.GetById("nonexistent").Should().BeNull();
    }

    [Fact]
    public void GetById_ShouldReturnNull_ForNullOrWhitespace()
    {
        var registry = new ConnectorProviderRegistry(Array.Empty<IConnectorProvider>());

        registry.GetById(null!).Should().BeNull();
        registry.GetById("").Should().BeNull();
        registry.GetById("   ").Should().BeNull();
    }

    [Fact]
    public void IsRegistered_ShouldReturnTrue_WhenExists()
    {
        var github = new FakeProvider("github", ConnectorType.GitHubIssueIntake, ConnectorDirection.Context);
        var registry = new ConnectorProviderRegistry(new[] { github });

        registry.IsRegistered("github").Should().BeTrue();
    }

    [Fact]
    public void IsRegistered_ShouldReturnFalse_WhenNotExists()
    {
        var registry = new ConnectorProviderRegistry(Array.Empty<IConnectorProvider>());

        registry.IsRegistered("github").Should().BeFalse();
    }

    [Fact]
    public void IsRegistered_ShouldReturnFalse_ForNullOrWhitespace()
    {
        var registry = new ConnectorProviderRegistry(Array.Empty<IConnectorProvider>());

        registry.IsRegistered(null!).Should().BeFalse();
        registry.IsRegistered("").Should().BeFalse();
    }

    [Fact]
    public void Constructor_ShouldSkipProvidersWithEmptyId()
    {
        var providers = new IConnectorProvider[]
        {
            new FakeProvider("", ConnectorType.Custom, ConnectorDirection.Inbound),
            new FakeProvider("github", ConnectorType.GitHubIssueIntake, ConnectorDirection.Context),
        };

        var registry = new ConnectorProviderRegistry(providers);

        registry.GetAll().Should().HaveCount(1);
        registry.GetAll()[0].ProviderId.Should().Be("github");
    }

    [Fact]
    public void Constructor_ShouldHandleDuplicateProviderIds_LastWins()
    {
        var first = new FakeProvider("github", ConnectorType.Custom, ConnectorDirection.Inbound);
        var second = new FakeProvider("github", ConnectorType.GitHubIssueIntake, ConnectorDirection.Context);

        var registry = new ConnectorProviderRegistry(new[] { first, second });

        registry.GetAll().Should().HaveCount(1);
        registry.GetById("github")!.ConnectorType.Should().Be(ConnectorType.GitHubIssueIntake);
    }

    private sealed class FakeProvider : IConnectorProvider
    {
        public string ProviderId { get; }
        public ConnectorType ConnectorType { get; }
        public ConnectorDirection Direction { get; }

        public FakeProvider(string providerId, ConnectorType type, ConnectorDirection direction)
        {
            ProviderId = providerId;
            ConnectorType = type;
            Direction = direction;
        }

        public Task<ConnectorHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ConnectorHealthResult.Healthy());

        public Task<ConnectorCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ConnectorCapabilities(
                ProviderId, Direction, Array.Empty<string>(),
                Array.Empty<ConnectorAuthMethod>()));
    }
}
