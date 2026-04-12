using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Api.Tests;

public class TelemetryConfigurationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public TelemetryConfigurationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void SentrySettings_ShouldBeRegisteredInDI()
    {
        var settings = _factory.Services.GetRequiredService<SentrySettings>();
        settings.Should().NotBeNull();
    }

    [Fact]
    public void SentrySettings_ShouldBeDisabledByDefault()
    {
        var settings = _factory.Services.GetRequiredService<SentrySettings>();
        settings.Enabled.Should().BeFalse();
    }

    [Fact]
    public void SentrySettings_SendDefaultPii_ShouldBeFalseByDefault()
    {
        var settings = _factory.Services.GetRequiredService<SentrySettings>();
        settings.SendDefaultPii.Should().BeFalse();
    }

    [Fact]
    public void TelemetrySettings_ShouldBeRegisteredInDI()
    {
        var settings = _factory.Services.GetRequiredService<TelemetrySettings>();
        settings.Should().NotBeNull();
    }

    [Fact]
    public void TelemetrySettings_ShouldBeDisabledByDefault()
    {
        var settings = _factory.Services.GetRequiredService<TelemetrySettings>();
        settings.Enabled.Should().BeFalse();
    }

    [Fact]
    public void AnalyticsSettings_ShouldBeRegisteredInDI()
    {
        var settings = _factory.Services.GetRequiredService<AnalyticsSettings>();
        settings.Should().NotBeNull();
    }

    [Fact]
    public void AnalyticsSettings_ShouldBeDisabledByDefault()
    {
        var settings = _factory.Services.GetRequiredService<AnalyticsSettings>();
        settings.Enabled.Should().BeFalse();
    }

    [Fact]
    public void TelemetryEventService_ShouldBeRegisteredInDI()
    {
        var service = _factory.Services.GetRequiredService<ITelemetryEventService>();
        service.Should().NotBeNull();
    }

    [Fact]
    public void TelemetryEventService_ShouldBeDisabledByDefault()
    {
        var service = _factory.Services.GetRequiredService<ITelemetryEventService>();
        service.IsEnabled.Should().BeFalse();
    }
}
