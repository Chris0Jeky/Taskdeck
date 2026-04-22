using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class IntegrationRegistryServiceTests
{
    private readonly Mock<IIntegrationConnectorRepository> _connectorRepoMock;
    private readonly Mock<IConnectorEventRepository> _eventRepoMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly IntegrationRegistryService _service;

    public IntegrationRegistryServiceTests()
    {
        _connectorRepoMock = new Mock<IIntegrationConnectorRepository>();
        _eventRepoMock = new Mock<IConnectorEventRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _service = new IntegrationRegistryService(
            _connectorRepoMock.Object,
            _eventRepoMock.Object,
            _unitOfWorkMock.Object);
    }

    [Fact]
    public async Task RegisterConnector_ShouldCreateConnectorAndEvent()
    {
        var userId = Guid.NewGuid();
        var dto = new CreateIntegrationConnectorDto(
            "My Connector",
            ConnectorType.BrowserClipper,
            ConnectorDirection.Inbound,
            """{"key": "value"}""");

        _connectorRepoMock
            .Setup(r => r.AddAsync(It.IsAny<IntegrationConnector>(), default))
            .ReturnsAsync((IntegrationConnector c, CancellationToken _) => c);
        _eventRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ConnectorEvent>(), default))
            .ReturnsAsync((ConnectorEvent e, CancellationToken _) => e);

        var result = await _service.RegisterConnectorAsync(userId, dto);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("My Connector");
        result.Value.ConnectorType.Should().Be(ConnectorType.BrowserClipper);
        result.Value.Direction.Should().Be(ConnectorDirection.Inbound);
        result.Value.Status.Should().Be(ConnectorStatus.Active);
        result.Value.Configuration.Should().Be("""{"key": "value"}""");

        _connectorRepoMock.Verify(r => r.AddAsync(It.IsAny<IntegrationConnector>(), default), Times.Once);
        _eventRepoMock.Verify(r => r.AddAsync(It.Is<ConnectorEvent>(e => e.EventType == ConnectorEventType.Connected), default), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task RegisterConnector_ShouldReturnFailure_WhenNameIsEmpty()
    {
        var dto = new CreateIntegrationConnectorDto(
            "",
            ConnectorType.Custom,
            ConnectorDirection.Inbound);

        var result = await _service.RegisterConnectorAsync(Guid.NewGuid(), dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public async Task ListConnectors_ShouldReturnUserConnectors()
    {
        var userId = Guid.NewGuid();
        var connectors = new List<IntegrationConnector>
        {
            new("Connector A", ConnectorType.WebClip, ConnectorDirection.Inbound, userId),
            new("Connector B", ConnectorType.Custom, ConnectorDirection.Outbound, userId),
        };

        _connectorRepoMock
            .Setup(r => r.GetByUserIdAsync(userId, default))
            .ReturnsAsync(connectors);

        var result = await _service.ListConnectorsAsync(userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].Name.Should().Be("Connector A");
        result.Value[1].Name.Should().Be("Connector B");
    }

    [Fact]
    public async Task GetConnector_ShouldReturnDetailWithEvents()
    {
        var userId = Guid.NewGuid();
        var connector = new IntegrationConnector("Test", ConnectorType.Custom, ConnectorDirection.Inbound, userId);
        var events = new List<ConnectorEvent>
        {
            new(connector.Id, ConnectorEventType.Connected, "Registered"),
        };

        _connectorRepoMock
            .Setup(r => r.GetByIdForUserAsync(connector.Id, userId, default))
            .ReturnsAsync(connector);
        _eventRepoMock
            .Setup(r => r.GetRecentByConnectorIdAsync(connector.Id, 20, default))
            .ReturnsAsync(events);

        var result = await _service.GetConnectorAsync(connector.Id, userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Test");
        result.Value.RecentEvents.Should().HaveCount(1);
        result.Value.RecentEvents[0].EventType.Should().Be(ConnectorEventType.Connected);
    }

    [Fact]
    public async Task GetConnector_ShouldReturnNotFound_WhenConnectorMissing()
    {
        _connectorRepoMock
            .Setup(r => r.GetByIdForUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), default))
            .ReturnsAsync((IntegrationConnector?)null);

        var result = await _service.GetConnectorAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task UpdateConnector_ShouldUpdateNameAndConfig()
    {
        var userId = Guid.NewGuid();
        var connector = new IntegrationConnector("Old Name", ConnectorType.Custom, ConnectorDirection.Inbound, userId);

        _connectorRepoMock
            .Setup(r => r.GetByIdForUserAsync(connector.Id, userId, default))
            .ReturnsAsync(connector);

        var dto = new UpdateIntegrationConnectorDto("New Name", """{"updated": true}""");
        var result = await _service.UpdateConnectorAsync(connector.Id, userId, dto);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("New Name");
        result.Value.Configuration.Should().Be("""{"updated": true}""");

        _connectorRepoMock.Verify(r => r.UpdateAsync(connector, default), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task UpdateConnector_ShouldReturnNotFound_WhenConnectorMissing()
    {
        _connectorRepoMock
            .Setup(r => r.GetByIdForUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), default))
            .ReturnsAsync((IntegrationConnector?)null);

        var result = await _service.UpdateConnectorAsync(Guid.NewGuid(), Guid.NewGuid(),
            new UpdateIntegrationConnectorDto("X"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task DeleteConnector_ShouldRemoveConnector()
    {
        var userId = Guid.NewGuid();
        var connector = new IntegrationConnector("ToDelete", ConnectorType.Custom, ConnectorDirection.Inbound, userId);

        _connectorRepoMock
            .Setup(r => r.GetByIdForUserAsync(connector.Id, userId, default))
            .ReturnsAsync(connector);

        var result = await _service.DeleteConnectorAsync(connector.Id, userId);

        result.IsSuccess.Should().BeTrue();
        _connectorRepoMock.Verify(r => r.DeleteAsync(connector, default), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task DeleteConnector_ShouldReturnNotFound_WhenConnectorMissing()
    {
        _connectorRepoMock
            .Setup(r => r.GetByIdForUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), default))
            .ReturnsAsync((IntegrationConnector?)null);

        var result = await _service.DeleteConnectorAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task EnableConnector_ShouldSetStatusAndLogEvent()
    {
        var userId = Guid.NewGuid();
        var connector = new IntegrationConnector("Test", ConnectorType.Custom, ConnectorDirection.Inbound, userId);
        connector.Disable();

        _connectorRepoMock
            .Setup(r => r.GetByIdForUserAsync(connector.Id, userId, default))
            .ReturnsAsync(connector);
        _eventRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ConnectorEvent>(), default))
            .ReturnsAsync((ConnectorEvent e, CancellationToken _) => e);

        var result = await _service.EnableConnectorAsync(connector.Id, userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ConnectorStatus.Active);
        _eventRepoMock.Verify(r => r.AddAsync(
            It.Is<ConnectorEvent>(e => e.EventType == ConnectorEventType.Connected),
            default), Times.Once);
    }

    [Fact]
    public async Task EnableConnector_ShouldReturnFailure_WhenAlreadyActive()
    {
        var userId = Guid.NewGuid();
        var connector = new IntegrationConnector("Test", ConnectorType.Custom, ConnectorDirection.Inbound, userId);

        _connectorRepoMock
            .Setup(r => r.GetByIdForUserAsync(connector.Id, userId, default))
            .ReturnsAsync(connector);

        var result = await _service.EnableConnectorAsync(connector.Id, userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("InvalidOperation");
    }

    [Fact]
    public async Task DisableConnector_ShouldSetStatusAndLogEvent()
    {
        var userId = Guid.NewGuid();
        var connector = new IntegrationConnector("Test", ConnectorType.Custom, ConnectorDirection.Inbound, userId);

        _connectorRepoMock
            .Setup(r => r.GetByIdForUserAsync(connector.Id, userId, default))
            .ReturnsAsync(connector);
        _eventRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ConnectorEvent>(), default))
            .ReturnsAsync((ConnectorEvent e, CancellationToken _) => e);

        var result = await _service.DisableConnectorAsync(connector.Id, userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ConnectorStatus.Disabled);
        _eventRepoMock.Verify(r => r.AddAsync(
            It.Is<ConnectorEvent>(e => e.EventType == ConnectorEventType.Disconnected),
            default), Times.Once);
    }

    [Fact]
    public async Task DisableConnector_ShouldReturnNotFound_WhenConnectorMissing()
    {
        _connectorRepoMock
            .Setup(r => r.GetByIdForUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), default))
            .ReturnsAsync((IntegrationConnector?)null);

        var result = await _service.DisableConnectorAsync(Guid.NewGuid(), Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }
}
