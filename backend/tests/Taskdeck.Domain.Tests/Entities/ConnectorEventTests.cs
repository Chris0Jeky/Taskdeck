using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ConnectorEventTests
{
    [Fact]
    public void Constructor_ShouldSetProperties()
    {
        var connectorId = Guid.NewGuid();
        var connectorEvent = new ConnectorEvent(
            connectorId,
            ConnectorEventType.Connected,
            "Test payload");

        connectorEvent.ConnectorId.Should().Be(connectorId);
        connectorEvent.EventType.Should().Be(ConnectorEventType.Connected);
        connectorEvent.Payload.Should().Be("Test payload");
        connectorEvent.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Constructor_ShouldAllowNullPayload()
    {
        var connectorEvent = new ConnectorEvent(
            Guid.NewGuid(),
            ConnectorEventType.Disconnected);

        connectorEvent.Payload.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyConnectorId()
    {
        var act = () => new ConnectorEvent(
            Guid.Empty,
            ConnectorEventType.Connected);

        act.Should().Throw<DomainException>()
            .WithMessage("*Connector ID cannot be empty*");
    }

    [Fact]
    public void Constructor_ShouldTruncatePayloadExceeding1000Characters()
    {
        var longPayload = new string('X', 1500);

        var connectorEvent = new ConnectorEvent(
            Guid.NewGuid(),
            ConnectorEventType.DataReceived,
            longPayload);

        connectorEvent.Payload.Should().HaveLength(1000);
        connectorEvent.Payload.Should().Be(longPayload[..1000]);
    }

    [Fact]
    public void Constructor_ShouldAcceptPayloadExactly1000Characters()
    {
        var exactPayload = new string('Y', 1000);

        var connectorEvent = new ConnectorEvent(
            Guid.NewGuid(),
            ConnectorEventType.DataReceived,
            exactPayload);

        connectorEvent.Payload.Should().HaveLength(1000);
    }

    [Theory]
    [InlineData(ConnectorEventType.Connected)]
    [InlineData(ConnectorEventType.Disconnected)]
    [InlineData(ConnectorEventType.DataReceived)]
    [InlineData(ConnectorEventType.Error)]
    public void Constructor_ShouldAcceptAllEventTypes(ConnectorEventType eventType)
    {
        var connectorEvent = new ConnectorEvent(
            Guid.NewGuid(),
            eventType,
            "test");

        connectorEvent.EventType.Should().Be(eventType);
    }
}
