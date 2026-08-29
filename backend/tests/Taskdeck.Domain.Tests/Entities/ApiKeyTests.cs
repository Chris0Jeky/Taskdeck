using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ApiKeyTests
{
    [Fact]
    public void ApiKeyScope_Values_AreStable()
    {
        ((int)ApiKeyScope.None).Should().Be(0);
        ((int)ApiKeyScope.Read).Should().Be(1);
        ((int)ApiKeyScope.Propose).Should().Be(2);
        ((int)ApiKeyScope.Manage).Should().Be(4);
        ((int)ApiKeyScope.Full).Should().Be(7);
    }

    [Fact]
    public void Constructor_ValidInput_CreatesEntity()
    {
        var userId = Guid.NewGuid();
        var apiKey = new ApiKey(userId, "abc123hash", "tdsk_abc", "Test Key", ApiKeyScope.Full);

        apiKey.UserId.Should().Be(userId);
        apiKey.KeyHash.Should().Be("abc123hash");
        apiKey.KeyPrefix_.Should().Be("tdsk_abc");
        apiKey.Name.Should().Be("Test Key");
        apiKey.IsActive.Should().BeTrue();
        apiKey.RevokedAt.Should().BeNull();
        apiKey.ExpiresAt.Should().BeNull();
        apiKey.LastUsedAt.Should().BeNull();
        apiKey.Scopes.Should().Be(ApiKeyScope.Full);
    }

    [Fact]
    public void Constructor_WithExpiration_SetsExpiresAt()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);
        var apiKey = new ApiKey(Guid.NewGuid(), "hash", "tdsk_abc", "Key", ApiKeyScope.Full, expiresAt);

        apiKey.ExpiresAt.Should().Be(expiresAt);
        apiKey.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Constructor_EmptyUserId_Throws()
    {
        var act = () => new ApiKey(Guid.Empty, "hash", "tdsk_abc", "Key", ApiKeyScope.Full);

        act.Should().Throw<DomainException>()
            .WithMessage("*UserId*empty*");
    }

    [Fact]
    public void Constructor_EmptyKeyHash_Throws()
    {
        var act = () => new ApiKey(Guid.NewGuid(), "", "tdsk_abc", "Key", ApiKeyScope.Full);

        act.Should().Throw<DomainException>()
            .WithMessage("*Key hash*empty*");
    }

    [Fact]
    public void Constructor_EmptyName_Throws()
    {
        var act = () => new ApiKey(Guid.NewGuid(), "hash", "tdsk_abc", "", ApiKeyScope.Full);

        act.Should().Throw<DomainException>()
            .WithMessage("*name*empty*");
    }

    [Fact]
    public void Constructor_NameTooLong_Throws()
    {
        var longName = new string('x', 101);
        var act = () => new ApiKey(Guid.NewGuid(), "hash", "tdsk_abc", longName, ApiKeyScope.Full);

        act.Should().Throw<DomainException>()
            .WithMessage("*name*100*");
    }

    [Fact]
    public void Constructor_PastExpiration_Throws()
    {
        var pastDate = DateTimeOffset.UtcNow.AddDays(-1);
        var act = () => new ApiKey(Guid.NewGuid(), "hash", "tdsk_abc", "Key", ApiKeyScope.Full, pastDate);

        act.Should().Throw<DomainException>()
            .WithMessage("*future*");
    }

    [Fact]
    public void Revoke_SetsRevokedAt()
    {
        var apiKey = new ApiKey(Guid.NewGuid(), "hash", "tdsk_abc", "Key", ApiKeyScope.Full);

        apiKey.Revoke();

        apiKey.RevokedAt.Should().NotBeNull();
        apiKey.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Revoke_AlreadyRevoked_Throws()
    {
        var apiKey = new ApiKey(Guid.NewGuid(), "hash", "tdsk_abc", "Key", ApiKeyScope.Full);
        apiKey.Revoke();

        var act = () => apiKey.Revoke();

        act.Should().Throw<DomainException>()
            .WithMessage("*already revoked*");
    }

    [Fact]
    public void IsActive_RevokedKey_ReturnsFalse()
    {
        var apiKey = new ApiKey(Guid.NewGuid(), "hash", "tdsk_abc", "Key", ApiKeyScope.Full);
        apiKey.Revoke();

        apiKey.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(ApiKeyScope.Read)]
    [InlineData(ApiKeyScope.Propose)]
    [InlineData(ApiKeyScope.Manage)]
    [InlineData(ApiKeyScope.Read | ApiKeyScope.Propose)]
    [InlineData(ApiKeyScope.Full)]
    public void Constructor_KnownNonEmptyScopes_PreservesMask(ApiKeyScope scopes)
    {
        var apiKey = new ApiKey(Guid.NewGuid(), "hash", "tdsk_abc", "Key", scopes);

        apiKey.Scopes.Should().Be(scopes);
    }

    [Theory]
    [InlineData(ApiKeyScope.None)]
    [InlineData((ApiKeyScope)8)]
    [InlineData(ApiKeyScope.Read | (ApiKeyScope)8)]
    public void Constructor_NoneOrUnknownScopes_Throws(ApiKeyScope scopes)
    {
        var act = () => new ApiKey(Guid.NewGuid(), "hash", "tdsk_abc", "Key", scopes);

        act.Should().Throw<DomainException>()
            .WithMessage("*scopes*known*non-empty*");
    }

    [Fact]
    public void ApiKeyScopeRules_TryParseNames_BuildsIndependentCombination()
    {
        var parsed = ApiKeyScopeRules.TryParseNames(
            new[] { "Manage", "read" },
            out var scopes);

        parsed.Should().BeTrue();
        scopes.Should().Be(ApiKeyScope.Read | ApiKeyScope.Manage);
        ApiKeyScopeRules.ToNames(scopes).Should().Equal("read", "manage");
    }

    [Theory]
    [MemberData(nameof(InvalidScopeNames))]
    public void ApiKeyScopeRules_TryParseNames_RejectsOmittedEmptyOrUnknown(
        IEnumerable<string>? names)
    {
        ApiKeyScopeRules.TryParseNames(names, out var scopes).Should().BeFalse();
        scopes.Should().Be(ApiKeyScope.None);
    }

    [Fact]
    public void ApiKeyScopeRules_Includes_RequiresEveryRequestedBit()
    {
        var granted = ApiKeyScope.Read | ApiKeyScope.Propose;

        ApiKeyScopeRules.Includes(granted, ApiKeyScope.Read).Should().BeTrue();
        ApiKeyScopeRules.Includes(granted, ApiKeyScope.Propose).Should().BeTrue();
        ApiKeyScopeRules.Includes(granted, ApiKeyScope.Manage).Should().BeFalse();
        ApiKeyScopeRules.Includes(granted, ApiKeyScope.Full).Should().BeFalse();
        ApiKeyScopeRules.Includes(ApiKeyScope.Full, ApiKeyScope.Full).Should().BeTrue();
    }

    public static TheoryData<IEnumerable<string>?> InvalidScopeNames => new()
    {
        null,
        Array.Empty<string>(),
        new[] { "" },
        new[] { "none" },
        new[] { "full" },
        new[] { "read", "unknown" }
    };
}
