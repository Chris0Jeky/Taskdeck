using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ExternalLoginTests
{
    [Fact]
    public void Constructor_ShouldCreateValidExternalLogin()
    {
        var userId = Guid.NewGuid();
        var login = new ExternalLogin(userId, "GitHub", "12345", "The Octocat", "https://avatar.url");

        login.UserId.Should().Be(userId);
        login.Provider.Should().Be("GitHub");
        login.ProviderUserId.Should().Be("12345");
        login.ProviderDisplayName.Should().Be("The Octocat");
        login.AvatarUrl.Should().Be("https://avatar.url");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenUserIdIsEmpty()
    {
        var act = () => new ExternalLogin(Guid.Empty, "GitHub", "12345");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenProviderIsEmpty()
    {
        var act = () => new ExternalLogin(Guid.NewGuid(), "", "12345");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenProviderUserIdIsEmpty()
    {
        var act = () => new ExternalLogin(Guid.NewGuid(), "GitHub", "");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void UpdateProfile_ShouldUpdateDisplayNameAndAvatarUrl()
    {
        var login = new ExternalLogin(Guid.NewGuid(), "GitHub", "12345", "Old Name", "https://old.url");
        var originalUpdatedAt = login.UpdatedAt;

        login.UpdateProfile("New Name", "https://new.url");

        login.ProviderDisplayName.Should().Be("New Name");
        login.AvatarUrl.Should().Be("https://new.url");
        login.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);
    }

    [Fact]
    public void Constructor_ShouldAllowNullOptionalFields()
    {
        var login = new ExternalLogin(Guid.NewGuid(), "GitHub", "12345");

        login.ProviderDisplayName.Should().BeNull();
        login.AvatarUrl.Should().BeNull();
    }
}
