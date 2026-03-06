using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.AspNetCore;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ObservabilityConfigurationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ObservabilityConfigurationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void AspNetCoreTracing_ShouldDisableAutomaticRawExceptionRecording()
    {
        var options = _factory.Services
            .GetRequiredService<IOptionsMonitor<AspNetCoreTraceInstrumentationOptions>>()
            .CurrentValue;

        options.RecordException.Should().BeFalse();
    }
}
