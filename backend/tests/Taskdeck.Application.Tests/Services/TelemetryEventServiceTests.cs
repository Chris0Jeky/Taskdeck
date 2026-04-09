using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class TelemetryEventServiceTests
{
    private readonly TelemetrySettings _settings;
    private readonly Mock<ILogger<TelemetryEventService>> _loggerMock;

    public TelemetryEventServiceTests()
    {
        _settings = new TelemetrySettings { Enabled = true, MaxBatchSize = 100 };
        _loggerMock = new Mock<ILogger<TelemetryEventService>>();
    }

    private TelemetryEventService CreateService() => new(_settings, _loggerMock.Object);

    private static TelemetryEvent CreateValidEvent(string eventName = "capture.submitted") => new()
    {
        Event = eventName,
        Timestamp = "2026-04-09T12:00:00Z",
        SessionId = Guid.NewGuid().ToString(),
        WorkspaceMode = "guided",
        AppVersion = "0.1.0",
        Platform = "web",
    };

    [Fact]
    public void IsEnabled_ShouldReturnTrue_WhenSettingsEnabled()
    {
        var service = CreateService();
        service.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_ShouldReturnFalse_WhenSettingsDisabled()
    {
        _settings.Enabled = false;
        var service = CreateService();
        service.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void RecordEvent_ShouldReturnFalse_WhenDisabled()
    {
        _settings.Enabled = false;
        var service = CreateService();

        var result = service.RecordEvent(CreateValidEvent());
        result.Should().BeFalse();
    }

    [Fact]
    public void RecordEvent_ShouldReturnTrue_ForValidEvent()
    {
        var service = CreateService();

        var result = service.RecordEvent(CreateValidEvent());
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("capture.submitted")]
    [InlineData("proposal.approved")]
    [InlineData("board.loaded")]
    [InlineData("auth_session.started")]
    [InlineData("agent_run.completed")]
    [InlineData("error.unhandled")]
    [InlineData("first_run.wizard_completed")]
    public void RecordEvent_ShouldAcceptValidTaxonomyNames(string eventName)
    {
        var service = CreateService();

        var result = service.RecordEvent(CreateValidEvent(eventName));
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("UPPER.CASE")]
    [InlineData("three.dot.name")]
    [InlineData("no-dashes.allowed")]
    [InlineData(".leading_dot")]
    [InlineData("trailing_dot.")]
    public void RecordEvent_ShouldRejectInvalidEventNames(string eventName)
    {
        var service = CreateService();

        var result = service.RecordEvent(CreateValidEvent(eventName));
        result.Should().BeFalse();
    }

    [Fact]
    public void RecordEvent_ShouldRejectEmptySessionId()
    {
        var service = CreateService();
        var evt = CreateValidEvent();
        evt.SessionId = "";

        var result = service.RecordEvent(evt);
        result.Should().BeFalse();
    }

    [Fact]
    public void RecordEvents_ShouldReturnZero_WhenDisabled()
    {
        _settings.Enabled = false;
        var service = CreateService();

        var result = service.RecordEvents(new List<TelemetryEvent> { CreateValidEvent() });
        result.Should().Be(0);
    }

    [Fact]
    public void RecordEvents_ShouldRecordAllValidEvents()
    {
        var service = CreateService();
        var events = new List<TelemetryEvent>
        {
            CreateValidEvent("capture.submitted"),
            CreateValidEvent("proposal.approved"),
            CreateValidEvent("board.loaded"),
        };

        var result = service.RecordEvents(events);
        result.Should().Be(3);
    }

    [Fact]
    public void RecordEvents_ShouldRejectBatchExceedingMaxSize()
    {
        _settings.MaxBatchSize = 2;
        var service = CreateService();
        var events = new List<TelemetryEvent>
        {
            CreateValidEvent("capture.submitted"),
            CreateValidEvent("proposal.approved"),
            CreateValidEvent("board.loaded"),
        };

        var result = service.RecordEvents(events);
        result.Should().Be(0);
    }

    [Fact]
    public void RecordEvents_ShouldCountOnlyValidEvents()
    {
        var service = CreateService();
        var invalidEvent = CreateValidEvent("INVALID");
        var events = new List<TelemetryEvent>
        {
            CreateValidEvent("capture.submitted"),
            invalidEvent,
            CreateValidEvent("board.loaded"),
        };

        var result = service.RecordEvents(events);
        result.Should().Be(2);
    }

    [Fact]
    public void RecordEvent_ShouldAcceptEventWithProperties()
    {
        var service = CreateService();
        var evt = CreateValidEvent();
        evt.Properties = new Dictionary<string, object>
        {
            { "has_attachment", true },
            { "source", "manual" },
        };

        var result = service.RecordEvent(evt);
        result.Should().BeTrue();
    }
}
