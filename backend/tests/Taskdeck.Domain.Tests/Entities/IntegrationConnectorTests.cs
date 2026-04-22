using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class IntegrationConnectorTests
{
    [Fact]
    public void Constructor_ShouldSetDefaults()
    {
        var userId = Guid.NewGuid();
        var connector = new IntegrationConnector(
            "Test Connector",
            ConnectorType.BrowserClipper,
            ConnectorDirection.Inbound,
            userId);

        connector.Name.Should().Be("Test Connector");
        connector.ConnectorType.Should().Be(ConnectorType.BrowserClipper);
        connector.Direction.Should().Be(ConnectorDirection.Inbound);
        connector.Status.Should().Be(ConnectorStatus.Active);
        connector.UserId.Should().Be(userId);
        connector.Configuration.Should().BeNull();
        connector.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Constructor_ShouldAcceptConfiguration()
    {
        var connector = new IntegrationConnector(
            "Configured",
            ConnectorType.WebhookInbound,
            ConnectorDirection.Inbound,
            Guid.NewGuid(),
            """{"url": "https://example.com"}""");

        connector.Configuration.Should().Be("""{"url": "https://example.com"}""");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_ShouldRejectEmptyName(string? name)
    {
        var act = () => new IntegrationConnector(
            name!,
            ConnectorType.Custom,
            ConnectorDirection.Inbound,
            Guid.NewGuid());

        act.Should().Throw<DomainException>()
            .WithMessage("*name cannot be empty*");
    }

    [Fact]
    public void Constructor_ShouldRejectNameExceeding100Characters()
    {
        var longName = new string('A', 101);

        var act = () => new IntegrationConnector(
            longName,
            ConnectorType.Custom,
            ConnectorDirection.Inbound,
            Guid.NewGuid());

        act.Should().Throw<DomainException>()
            .WithMessage("*100 characters*");
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyUserId()
    {
        var act = () => new IntegrationConnector(
            "Test",
            ConnectorType.Custom,
            ConnectorDirection.Inbound,
            Guid.Empty);

        act.Should().Throw<DomainException>()
            .WithMessage("*User ID cannot be empty*");
    }

    [Fact]
    public void Constructor_ShouldRejectConfigurationExceeding4000Characters()
    {
        var longConfig = new string('x', 4001);

        var act = () => new IntegrationConnector(
            "Test",
            ConnectorType.Custom,
            ConnectorDirection.Inbound,
            Guid.NewGuid(),
            longConfig);

        act.Should().Throw<DomainException>()
            .WithMessage("*4000 characters*");
    }

    [Fact]
    public void Constructor_ShouldTrimName()
    {
        var connector = new IntegrationConnector(
            "  Trimmed Name  ",
            ConnectorType.Custom,
            ConnectorDirection.Inbound,
            Guid.NewGuid());

        connector.Name.Should().Be("Trimmed Name");
    }

    [Fact]
    public void UpdateName_ShouldChangeName()
    {
        var connector = new IntegrationConnector(
            "Original",
            ConnectorType.Custom,
            ConnectorDirection.Inbound,
            Guid.NewGuid());

        var originalUpdatedAt = connector.UpdatedAt;

        connector.UpdateName("Updated");

        connector.Name.Should().Be("Updated");
        connector.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);
    }

    [Fact]
    public void UpdateConfiguration_ShouldChangeConfig()
    {
        var connector = new IntegrationConnector(
            "Test",
            ConnectorType.Custom,
            ConnectorDirection.Inbound,
            Guid.NewGuid());

        connector.UpdateConfiguration("""{"key": "value"}""");

        connector.Configuration.Should().Be("""{"key": "value"}""");
    }

    [Fact]
    public void Enable_ShouldSetStatusToActive()
    {
        var connector = new IntegrationConnector(
            "Test",
            ConnectorType.Custom,
            ConnectorDirection.Inbound,
            Guid.NewGuid());

        connector.Disable();
        connector.Enable();

        connector.Status.Should().Be(ConnectorStatus.Active);
    }

    [Fact]
    public void Enable_ShouldThrow_WhenAlreadyActive()
    {
        var connector = new IntegrationConnector(
            "Test",
            ConnectorType.Custom,
            ConnectorDirection.Inbound,
            Guid.NewGuid());

        var act = () => connector.Enable();

        act.Should().Throw<DomainException>()
            .WithMessage("*already active*");
    }

    [Fact]
    public void Disable_ShouldSetStatusToDisabled()
    {
        var connector = new IntegrationConnector(
            "Test",
            ConnectorType.Custom,
            ConnectorDirection.Inbound,
            Guid.NewGuid());

        connector.Disable();

        connector.Status.Should().Be(ConnectorStatus.Disabled);
    }

    [Fact]
    public void Disable_ShouldThrow_WhenAlreadyDisabled()
    {
        var connector = new IntegrationConnector(
            "Test",
            ConnectorType.Custom,
            ConnectorDirection.Inbound,
            Guid.NewGuid());

        connector.Disable();

        var act = () => connector.Disable();

        act.Should().Throw<DomainException>()
            .WithMessage("*already disabled*");
    }

    [Fact]
    public void MarkError_ShouldSetStatusToError()
    {
        var connector = new IntegrationConnector(
            "Test",
            ConnectorType.Custom,
            ConnectorDirection.Inbound,
            Guid.NewGuid());

        connector.MarkError();

        connector.Status.Should().Be(ConnectorStatus.Error);
    }

    [Fact]
    public void MarkError_ThenEnable_ShouldSetStatusToActive()
    {
        var connector = new IntegrationConnector(
            "Test",
            ConnectorType.Custom,
            ConnectorDirection.Inbound,
            Guid.NewGuid());

        connector.MarkError();
        connector.Enable();

        connector.Status.Should().Be(ConnectorStatus.Active);
    }

    [Theory]
    [InlineData(ConnectorType.BrowserClipper)]
    [InlineData(ConnectorType.MarkdownImport)]
    [InlineData(ConnectorType.WebClip)]
    [InlineData(ConnectorType.GitHubIssueIntake)]
    [InlineData(ConnectorType.WebhookInbound)]
    [InlineData(ConnectorType.Custom)]
    public void Constructor_ShouldAcceptAllConnectorTypes(ConnectorType type)
    {
        var connector = new IntegrationConnector(
            "Test",
            type,
            ConnectorDirection.Inbound,
            Guid.NewGuid());

        connector.ConnectorType.Should().Be(type);
    }

    [Theory]
    [InlineData(ConnectorDirection.Inbound)]
    [InlineData(ConnectorDirection.Context)]
    [InlineData(ConnectorDirection.Outbound)]
    public void Constructor_ShouldAcceptAllDirections(ConnectorDirection direction)
    {
        var connector = new IntegrationConnector(
            "Test",
            ConnectorType.Custom,
            direction,
            Guid.NewGuid());

        connector.Direction.Should().Be(direction);
    }
}
