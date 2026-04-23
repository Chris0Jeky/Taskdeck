using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Taskdeck.Api.Tests;

public class LoggingProviderConfigurationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LoggingProviderConfigurationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Host_DoesNotRegisterWindowsEventLogProvider()
    {
        var providers = _factory.Services.GetServices<ILoggerProvider>().ToList();

        providers.Should().NotContain(
            p => p.GetType().FullName!.Contains("EventLog", StringComparison.OrdinalIgnoreCase),
            "Windows EventLog provider causes ObjectDisposedException crashes in background workers");
    }
}
