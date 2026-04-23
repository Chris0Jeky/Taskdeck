using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Application.Connectors;
using Taskdeck.Domain.Connectors;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Connectors;

public class ConnectorExecutionServiceTests
{
    private readonly ILogger<ConnectorExecutionService> _logger = NullLogger<ConnectorExecutionService>.Instance;

    [Fact]
    public async Task CheckProviderHealthAsync_ShouldReturnHealth_WhenProviderExists()
    {
        var provider = new FakeProvider("github", ConnectorHealthResult.Healthy("OK"));
        var registry = new ConnectorProviderRegistry(new[] { provider });
        var service = new ConnectorExecutionService(registry, _logger);

        var result = await service.CheckProviderHealthAsync("github");

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ConnectorHealthStatus.Healthy);
        result.Value.Message.Should().Be("OK");
    }

    [Fact]
    public async Task CheckProviderHealthAsync_ShouldReturnNotFound_WhenProviderDoesNotExist()
    {
        var registry = new ConnectorProviderRegistry(Array.Empty<IConnectorProvider>());
        var service = new ConnectorExecutionService(registry, _logger);

        var result = await service.CheckProviderHealthAsync("nonexistent");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task CheckProviderHealthAsync_ShouldRetryOnHttpError()
    {
        var callCount = 0;
        var provider = new CallbackProvider("test", () =>
        {
            callCount++;
            if (callCount < 3)
                throw new HttpRequestException("Network error");
            return Task.FromResult(ConnectorHealthResult.Healthy("Recovered"));
        });

        var registry = new ConnectorProviderRegistry(new[] { (IConnectorProvider)provider });
        var service = new ConnectorExecutionService(registry, _logger);

        var result = await service.CheckProviderHealthAsync("test");

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ConnectorHealthStatus.Healthy);
        callCount.Should().Be(3);
    }

    [Fact]
    public async Task CheckProviderHealthAsync_ShouldNotRetryOnNonTransientError()
    {
        var callCount = 0;
        var provider = new CallbackProvider("test", () =>
        {
            callCount++;
            throw new InvalidOperationException("Non-transient");
        });

        var registry = new ConnectorProviderRegistry(new[] { (IConnectorProvider)provider });
        var service = new ConnectorExecutionService(registry, _logger);

        var result = await service.CheckProviderHealthAsync("test");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("UnexpectedError");
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task GetProviderCapabilitiesAsync_ShouldReturnCapabilities()
    {
        var provider = new FakeProvider("github", ConnectorHealthResult.Healthy());
        var registry = new ConnectorProviderRegistry(new[] { provider });
        var service = new ConnectorExecutionService(registry, _logger);

        var result = await service.GetProviderCapabilitiesAsync("github");

        result.IsSuccess.Should().BeTrue();
        result.Value.DisplayName.Should().Be("github");
    }

    [Fact]
    public async Task GetProviderCapabilitiesAsync_ShouldReturnNotFound_WhenProviderDoesNotExist()
    {
        var registry = new ConnectorProviderRegistry(Array.Empty<IConnectorProvider>());
        var service = new ConnectorExecutionService(registry, _logger);

        var result = await service.GetProviderCapabilitiesAsync("nonexistent");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    private sealed class FakeProvider : IConnectorProvider
    {
        private readonly ConnectorHealthResult _healthResult;

        public string ProviderId { get; }
        public ConnectorType ConnectorType => ConnectorType.GitHubIssueIntake;
        public ConnectorDirection Direction => ConnectorDirection.Context;

        public FakeProvider(string providerId, ConnectorHealthResult healthResult)
        {
            ProviderId = providerId;
            _healthResult = healthResult;
        }

        public Task<ConnectorHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_healthResult);

        public Task<ConnectorCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ConnectorCapabilities(
                ProviderId, Direction, Array.Empty<string>(),
                Array.Empty<ConnectorAuthMethod>()));
    }

    private sealed class CallbackProvider : IConnectorProvider
    {
        private readonly Func<Task<ConnectorHealthResult>> _healthCallback;

        public string ProviderId { get; }
        public ConnectorType ConnectorType => ConnectorType.Custom;
        public ConnectorDirection Direction => ConnectorDirection.Context;

        public CallbackProvider(string providerId, Func<Task<ConnectorHealthResult>> healthCallback)
        {
            ProviderId = providerId;
            _healthCallback = healthCallback;
        }

        public Task<ConnectorHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
            => _healthCallback();

        public Task<ConnectorCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ConnectorCapabilities(
                ProviderId, Direction, Array.Empty<string>(),
                Array.Empty<ConnectorAuthMethod>()));
    }
}
