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

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://insecure.example.com/avatar.png")]
    [InlineData("ftp://files.example.com/avatar.png")]
    [InlineData("data:image/png;base64,abc")]
    public void Constructor_ShouldDiscardInvalidAvatarUrls(string invalidUrl)
    {
        var login = new ExternalLogin(Guid.NewGuid(), "GitHub", "12345", "Name", invalidUrl);

        login.AvatarUrl.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldAcceptValidHttpsAvatarUrl()
    {
        var login = new ExternalLogin(Guid.NewGuid(), "GitHub", "12345", "Name", "https://avatars.githubusercontent.com/u/12345");

        login.AvatarUrl.Should().Be("https://avatars.githubusercontent.com/u/12345");
    }

    [Fact]
    public void Constructor_ShouldStripControlCharsFromDisplayName()
    {
        var login = new ExternalLogin(Guid.NewGuid(), "GitHub", "12345", "Name\0With\aControl");

        login.ProviderDisplayName.Should().Be("NameWithControl");
    }

    [Fact]
    public void UpdateProfile_ShouldDiscardInvalidAvatarUrl()
    {
        var login = new ExternalLogin(Guid.NewGuid(), "GitHub", "12345", "Name", "https://valid.url");

        login.UpdateProfile("New Name", "javascript:alert(1)");

        login.AvatarUrl.Should().BeNull();
        login.ProviderDisplayName.Should().Be("New Name");
    }
}
