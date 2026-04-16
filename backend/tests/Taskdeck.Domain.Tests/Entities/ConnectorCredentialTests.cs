using FluentAssertions;
using Taskdeck.Domain.Connectors;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ConnectorCredentialTests
{
    [Fact]
    public void Constructor_ShouldSetAllProperties()
    {
        var connectorId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var credential = new ConnectorCredential(
            connectorId,
            userId,
            ConnectorAuthMethod.PersonalAccessToken,
            "GitHub PAT",
            "encrypted-value-here");

        credential.ConnectorId.Should().Be(connectorId);
        credential.UserId.Should().Be(userId);
        credential.AuthMethod.Should().Be(ConnectorAuthMethod.PersonalAccessToken);
        credential.Label.Should().Be("GitHub PAT");
        credential.EncryptedValue.Should().Be("encrypted-value-here");
        credential.RotatedAt.Should().BeNull();
        credential.ExpiresAt.Should().BeNull();
        credential.IsExpired.Should().BeFalse();
        credential.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Constructor_ShouldAcceptExpiresAt()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);

        var credential = new ConnectorCredential(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ConnectorAuthMethod.OAuth2,
            "OAuth Token",
            "encrypted",
            expiresAt);

        credential.ExpiresAt.Should().Be(expiresAt);
        credential.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyConnectorId()
    {
        var act = () => new ConnectorCredential(
            Guid.Empty,
            Guid.NewGuid(),
            ConnectorAuthMethod.ApiKey,
            "Key",
            "encrypted");

        act.Should().Throw<DomainException>()
            .WithMessage("*Connector ID cannot be empty*");
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyUserId()
    {
        var act = () => new ConnectorCredential(
            Guid.NewGuid(),
            Guid.Empty,
            ConnectorAuthMethod.ApiKey,
            "Key",
            "encrypted");

        act.Should().Throw<DomainException>()
            .WithMessage("*User ID cannot be empty*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_ShouldRejectEmptyLabel(string? label)
    {
        var act = () => new ConnectorCredential(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ConnectorAuthMethod.ApiKey,
            label!,
            "encrypted");

        act.Should().Throw<DomainException>()
            .WithMessage("*label cannot be empty*");
    }

    [Fact]
    public void Constructor_ShouldRejectLabelExceeding100Characters()
    {
        var longLabel = new string('a', 101);

        var act = () => new ConnectorCredential(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ConnectorAuthMethod.ApiKey,
            longLabel,
            "encrypted");

        act.Should().Throw<DomainException>()
            .WithMessage("*label cannot exceed 100*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_ShouldRejectEmptyEncryptedValue(string? value)
    {
        var act = () => new ConnectorCredential(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ConnectorAuthMethod.ApiKey,
            "Key",
            value!);

        act.Should().Throw<DomainException>()
            .WithMessage("*Encrypted value cannot be empty*");
    }

    [Fact]
    public void Constructor_ShouldRejectEncryptedValueExceeding8000Characters()
    {
        var longValue = new string('x', 8001);

        var act = () => new ConnectorCredential(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ConnectorAuthMethod.ApiKey,
            "Key",
            longValue);

        act.Should().Throw<DomainException>()
            .WithMessage("*Encrypted value cannot exceed 8000*");
    }

    [Fact]
    public void Rotate_ShouldUpdateValueAndTimestamp()
    {
        var credential = new ConnectorCredential(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ConnectorAuthMethod.PersonalAccessToken,
            "Token",
            "old-encrypted");

        credential.Rotate("new-encrypted");

        credential.EncryptedValue.Should().Be("new-encrypted");
        credential.RotatedAt.Should().NotBeNull();
        credential.RotatedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Rotate_ShouldUpdateExpiresAt()
    {
        var credential = new ConnectorCredential(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ConnectorAuthMethod.OAuth2,
            "Token",
            "old-encrypted",
            DateTimeOffset.UtcNow.AddDays(1));

        var newExpiry = DateTimeOffset.UtcNow.AddDays(30);
        credential.Rotate("new-encrypted", newExpiry);

        credential.ExpiresAt.Should().Be(newExpiry);
    }

    [Fact]
    public void Rotate_ShouldRejectEmptyValue()
    {
        var credential = new ConnectorCredential(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ConnectorAuthMethod.ApiKey,
            "Key",
            "encrypted");

        var act = () => credential.Rotate("");

        act.Should().Throw<DomainException>()
            .WithMessage("*Encrypted value cannot be empty*");
    }

    [Fact]
    public void IsExpired_ShouldReturnTrue_WhenPastExpiresAt()
    {
        var credential = new ConnectorCredential(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ConnectorAuthMethod.ApiKey,
            "Key",
            "encrypted",
            DateTimeOffset.UtcNow.AddMinutes(-1));

        credential.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_ShouldReturnFalse_WhenNoExpiresAt()
    {
        var credential = new ConnectorCredential(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ConnectorAuthMethod.ApiKey,
            "Key",
            "encrypted");

        credential.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void Label_ShouldTrimWhitespace()
    {
        var credential = new ConnectorCredential(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ConnectorAuthMethod.ApiKey,
            "  Trimmed Label  ",
            "encrypted");

        credential.Label.Should().Be("Trimmed Label");
    }
}
