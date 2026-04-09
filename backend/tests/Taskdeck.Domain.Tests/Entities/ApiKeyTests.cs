using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ApiKeyTests
{
    [Fact]
    public void Constructor_ValidInput_CreatesEntity()
    {
        var userId = Guid.NewGuid();
        var apiKey = new ApiKey(userId, "abc123hash", "tdsk_abc", "Test Key");

        apiKey.UserId.Should().Be(userId);
        apiKey.KeyHash.Should().Be("abc123hash");
        apiKey.KeyPrefix_.Should().Be("tdsk_abc");
        apiKey.Name.Should().Be("Test Key");
        apiKey.IsActive.Should().BeTrue();
        apiKey.RevokedAt.Should().BeNull();
        apiKey.ExpiresAt.Should().BeNull();
        apiKey.LastUsedAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithExpiration_SetsExpiresAt()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);
        var apiKey = new ApiKey(Guid.NewGuid(), "hash", "tdsk_abc", "Key", expiresAt);

        apiKey.ExpiresAt.Should().Be(expiresAt);
        apiKey.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Constructor_EmptyUserId_Throws()
    {
        var act = () => new ApiKey(Guid.Empty, "hash", "tdsk_abc", "Key");

        act.Should().Throw<DomainException>()
            .WithMessage("*UserId*empty*");
    }

    [Fact]
    public void Constructor_EmptyKeyHash_Throws()
    {
        var act = () => new ApiKey(Guid.NewGuid(), "", "tdsk_abc", "Key");

        act.Should().Throw<DomainException>()
            .WithMessage("*Key hash*empty*");
    }

    [Fact]
    public void Constructor_EmptyName_Throws()
    {
        var act = () => new ApiKey(Guid.NewGuid(), "hash", "tdsk_abc", "");

        act.Should().Throw<DomainException>()
            .WithMessage("*name*empty*");
    }

    [Fact]
    public void Constructor_NameTooLong_Throws()
    {
        var longName = new string('x', 101);
        var act = () => new ApiKey(Guid.NewGuid(), "hash", "tdsk_abc", longName);

        act.Should().Throw<DomainException>()
            .WithMessage("*name*100*");
    }

    [Fact]
    public void Constructor_PastExpiration_Throws()
    {
        var pastDate = DateTimeOffset.UtcNow.AddDays(-1);
        var act = () => new ApiKey(Guid.NewGuid(), "hash", "tdsk_abc", "Key", pastDate);

        act.Should().Throw<DomainException>()
            .WithMessage("*future*");
    }

    [Fact]
    public void Revoke_SetsRevokedAt()
    {
        var apiKey = new ApiKey(Guid.NewGuid(), "hash", "tdsk_abc", "Key");

        apiKey.Revoke();

        apiKey.RevokedAt.Should().NotBeNull();
        apiKey.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Revoke_AlreadyRevoked_Throws()
    {
        var apiKey = new ApiKey(Guid.NewGuid(), "hash", "tdsk_abc", "Key");
        apiKey.Revoke();

        var act = () => apiKey.Revoke();

        act.Should().Throw<DomainException>()
            .WithMessage("*already revoked*");
    }

    [Fact]
    public void RecordUsage_UpdatesLastUsedAt()
    {
        var apiKey = new ApiKey(Guid.NewGuid(), "hash", "tdsk_abc", "Key");

        apiKey.RecordUsage();

        apiKey.LastUsedAt.Should().NotBeNull();
        apiKey.LastUsedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void IsActive_RevokedKey_ReturnsFalse()
    {
        var apiKey = new ApiKey(Guid.NewGuid(), "hash", "tdsk_abc", "Key");
        apiKey.Revoke();

        apiKey.IsActive.Should().BeFalse();
    }
}
