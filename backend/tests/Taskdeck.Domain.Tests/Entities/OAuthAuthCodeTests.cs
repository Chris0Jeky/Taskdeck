using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class OAuthAuthCodeTests
{
    [Fact]
    public void Constructor_ValidParameters_CreatesEntity()
    {
        var userId = Guid.NewGuid();
        var code = "test-code-abc123";
        var token = "jwt-token-value";
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(60);

        var authCode = new OAuthAuthCode(code, userId, token, expiresAt);

        authCode.Code.Should().Be(code);
        authCode.UserId.Should().Be(userId);
        authCode.Token.Should().Be(token);
        authCode.ExpiresAt.Should().Be(expiresAt);
        authCode.IsConsumed.Should().BeFalse();
        authCode.ConsumedAt.Should().BeNull();
        authCode.Purpose.Should().Be("login");
        authCode.IsLinkingCode.Should().BeFalse();
    }

    [Fact]
    public void Constructor_EmptyCode_Throws()
    {
        var act = () => new OAuthAuthCode("", Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddSeconds(60));
        act.Should().Throw<DomainException>().WithMessage("*Auth code cannot be empty*");
    }

    [Fact]
    public void Constructor_EmptyUserId_Throws()
    {
        var act = () => new OAuthAuthCode("code", Guid.Empty, "token", DateTimeOffset.UtcNow.AddSeconds(60));
        act.Should().Throw<DomainException>().WithMessage("*User ID cannot be empty*");
    }

    [Fact]
    public void Constructor_EmptyToken_Throws()
    {
        var act = () => new OAuthAuthCode("code", Guid.NewGuid(), "", DateTimeOffset.UtcNow.AddSeconds(60));
        act.Should().Throw<DomainException>().WithMessage("*Token cannot be empty*");
    }

    [Fact]
    public void Constructor_PastExpiry_Throws()
    {
        var act = () => new OAuthAuthCode("code", Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddSeconds(-10));
        act.Should().Throw<DomainException>().WithMessage("*Expiry must be in the future*");
    }

    [Fact]
    public void Constructor_CodeTooLong_Throws()
    {
        var longCode = new string('x', 513);
        var act = () => new OAuthAuthCode(longCode, Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddSeconds(60));
        act.Should().Throw<DomainException>().WithMessage("*Auth code cannot exceed 512 characters*");
    }

    [Fact]
    public void TryConsume_ValidCode_ReturnsTrue()
    {
        var authCode = new OAuthAuthCode("code", Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddSeconds(60));

        var consumed = authCode.TryConsume();

        consumed.Should().BeTrue();
        authCode.IsConsumed.Should().BeTrue();
        authCode.ConsumedAt.Should().NotBeNull();
    }

    [Fact]
    public void TryConsume_AlreadyConsumed_ReturnsFalse()
    {
        var authCode = new OAuthAuthCode("code", Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddSeconds(60));
        authCode.TryConsume();

        var secondConsume = authCode.TryConsume();

        secondConsume.Should().BeFalse();
    }

    [Fact]
    public void TryConsume_Expired_ReturnsFalse()
    {
        var authCode = new OAuthAuthCode("code", Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddSeconds(60));

        // Force expiry via reflection
        var expiresAtProp = typeof(OAuthAuthCode).GetProperty("ExpiresAt");
        expiresAtProp!.SetValue(authCode, DateTimeOffset.UtcNow.AddSeconds(-10));

        var consumed = authCode.TryConsume();

        consumed.Should().BeFalse();
        authCode.IsConsumed.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_FutureExpiry_ReturnsFalse()
    {
        var authCode = new OAuthAuthCode("code", Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddSeconds(60));
        authCode.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_PastExpiry_ReturnsTrue()
    {
        var authCode = new OAuthAuthCode("code", Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddSeconds(60));

        var expiresAtProp = typeof(OAuthAuthCode).GetProperty("ExpiresAt");
        expiresAtProp!.SetValue(authCode, DateTimeOffset.UtcNow.AddSeconds(-10));

        authCode.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void CreateForLinking_ValidParameters_CreatesEntity()
    {
        var providerData = "{\"provider\":\"GitHub\",\"providerUserId\":\"12345\"}";
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(60);

        var linkCode = OAuthAuthCode.CreateForLinking("link-code", providerData, expiresAt);

        linkCode.Code.Should().Be("link-code");
        linkCode.Purpose.Should().Be("link");
        linkCode.IsLinkingCode.Should().BeTrue();
        linkCode.ProviderData.Should().Be(providerData);
        linkCode.UserId.Should().Be(Guid.Empty);
        linkCode.Token.Should().BeEmpty();
    }

    [Fact]
    public void CreateForLinking_EmptyProviderData_Throws()
    {
        var act = () => OAuthAuthCode.CreateForLinking("code", "", DateTimeOffset.UtcNow.AddSeconds(60));
        act.Should().Throw<DomainException>().WithMessage("*Provider data cannot be empty*");
    }

    [Fact]
    public void CreateForLinking_PastExpiry_Throws()
    {
        var act = () => OAuthAuthCode.CreateForLinking("code", "{}", DateTimeOffset.UtcNow.AddSeconds(-10));
        act.Should().Throw<DomainException>().WithMessage("*Expiry must be in the future*");
    }

    [Fact]
    public void CreateForLinking_TryConsume_WorksCorrectly()
    {
        var linkCode = OAuthAuthCode.CreateForLinking("link-code", "{}", DateTimeOffset.UtcNow.AddSeconds(60));

        var consumed = linkCode.TryConsume();

        consumed.Should().BeTrue();
        linkCode.IsConsumed.Should().BeTrue();

        // Second consume should fail
        linkCode.TryConsume().Should().BeFalse();
    }
}
