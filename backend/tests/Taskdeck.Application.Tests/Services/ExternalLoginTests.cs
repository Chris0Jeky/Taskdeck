using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ExternalLoginTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IExternalLoginRepository> _externalLoginRepoMock;

    public ExternalLoginTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _userRepoMock = new Mock<IUserRepository>();
        _externalLoginRepoMock = new Mock<IExternalLoginRepository>();

        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.ExternalLogins).Returns(_externalLoginRepoMock.Object);
    }

    [Fact]
    public async Task ExternalLoginAsync_ShouldCreateNewUser_WhenNoExistingAccountFound()
    {
        var service = CreateService();
        var dto = new ExternalLoginDto(
            Provider: "GitHub",
            ProviderUserId: "12345",
            Username: "octocat",
            Email: "octocat@github.com",
            DisplayName: "The Octocat",
            AvatarUrl: "https://avatars.githubusercontent.com/u/12345");

        _externalLoginRepoMock
            .Setup(r => r.GetByProviderAsync("GitHub", "12345", default))
            .ReturnsAsync((ExternalLogin?)null);

        _userRepoMock
            .Setup(r => r.GetByEmailAsync("octocat@github.com", default))
            .ReturnsAsync((User?)null);

        _userRepoMock
            .Setup(r => r.GetByUsernameAsync("octocat", default))
            .ReturnsAsync((User?)null);

        User? capturedUser = null;
        _userRepoMock
            .Setup(r => r.AddAsync(It.IsAny<User>(), default))
            .Callback<User, CancellationToken>((user, _) => capturedUser = user)
            .ReturnsAsync((User user, CancellationToken _) => user);

        ExternalLogin? capturedLogin = null;
        _externalLoginRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ExternalLogin>(), default))
            .Callback<ExternalLogin, CancellationToken>((login, _) => capturedLogin = login)
            .ReturnsAsync((ExternalLogin login, CancellationToken _) => login);

        var result = await service.ExternalLoginAsync(dto);

        result.IsSuccess.Should().BeTrue();
        result.Value.Token.Should().NotBeNullOrWhiteSpace();
        result.Value.User.Username.Should().Be("octocat");
        result.Value.User.Email.Should().Be("octocat@github.com");

        capturedUser.Should().NotBeNull();
        capturedUser!.Username.Should().Be("octocat");

        capturedLogin.Should().NotBeNull();
        capturedLogin!.Provider.Should().Be("GitHub");
        capturedLogin.ProviderUserId.Should().Be("12345");
        capturedLogin.ProviderDisplayName.Should().Be("The Octocat");
        capturedLogin.AvatarUrl.Should().Be("https://avatars.githubusercontent.com/u/12345");
    }

    [Fact]
    public async Task ExternalLoginAsync_ShouldReturnToken_WhenExternalLoginAlreadyLinked()
    {
        var service = CreateService();
        var existingUser = new User("octocat", "octocat@github.com", BCrypt.Net.BCrypt.HashPassword("random"));
        var existingLogin = new ExternalLogin(existingUser.Id, "GitHub", "12345", "The Octocat", "https://avatar.url");

        _externalLoginRepoMock
            .Setup(r => r.GetByProviderAsync("GitHub", "12345", default))
            .ReturnsAsync(existingLogin);

        _userRepoMock
            .Setup(r => r.GetByIdAsync(existingUser.Id, default))
            .ReturnsAsync(existingUser);

        var dto = new ExternalLoginDto(
            Provider: "GitHub",
            ProviderUserId: "12345",
            Username: "octocat",
            Email: "octocat@github.com");

        var result = await service.ExternalLoginAsync(dto);

        result.IsSuccess.Should().BeTrue();
        result.Value.Token.Should().NotBeNullOrWhiteSpace();
        result.Value.User.Id.Should().Be(existingUser.Id);

        // Should not create a new user
        _userRepoMock.Verify(r => r.AddAsync(It.IsAny<User>(), default), Times.Never);
    }

    [Fact]
    public async Task ExternalLoginAsync_ShouldCreateNewUser_WhenEmailMatchesExistingAccount_NoAutoLink()
    {
        // Security: Do NOT auto-link by email to prevent account takeover
        var service = CreateService();
        var existingUser = new User("local-user", "shared@example.com", BCrypt.Net.BCrypt.HashPassword("password"));

        _externalLoginRepoMock
            .Setup(r => r.GetByProviderAsync("GitHub", "99999", default))
            .ReturnsAsync((ExternalLogin?)null);

        _userRepoMock
            .Setup(r => r.GetByEmailAsync("shared@example.com", default))
            .ReturnsAsync(existingUser);

        // The generated email should be different from the existing user's
        _userRepoMock
            .Setup(r => r.GetByEmailAsync("github-99999@external.taskdeck.local", default))
            .ReturnsAsync((User?)null);

        _userRepoMock
            .Setup(r => r.GetByUsernameAsync("github-user", default))
            .ReturnsAsync((User?)null);

        User? capturedUser = null;
        _userRepoMock
            .Setup(r => r.AddAsync(It.IsAny<User>(), default))
            .Callback<User, CancellationToken>((user, _) => capturedUser = user)
            .ReturnsAsync((User user, CancellationToken _) => user);

        _externalLoginRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ExternalLogin>(), default))
            .ReturnsAsync((ExternalLogin login, CancellationToken _) => login);

        var dto = new ExternalLoginDto(
            Provider: "GitHub",
            ProviderUserId: "99999",
            Username: "github-user",
            Email: "shared@example.com");

        var result = await service.ExternalLoginAsync(dto);

        result.IsSuccess.Should().BeTrue();
        // Should create a NEW user, not link to existing
        capturedUser.Should().NotBeNull();
        capturedUser!.Email.Should().Be("github-99999@external.taskdeck.local");
        capturedUser.Username.Should().Be("github-user");

        // Should create a new user, not reuse existing
        _userRepoMock.Verify(r => r.AddAsync(It.IsAny<User>(), default), Times.Once);
    }

    [Fact]
    public async Task ExternalLoginAsync_ShouldAppendSuffix_WhenUsernameAlreadyTaken()
    {
        var service = CreateService();
        var existingUser = new User("octocat", "existing@example.com", BCrypt.Net.BCrypt.HashPassword("password"));

        _externalLoginRepoMock
            .Setup(r => r.GetByProviderAsync("GitHub", "55555", default))
            .ReturnsAsync((ExternalLogin?)null);

        _userRepoMock
            .Setup(r => r.GetByEmailAsync("new@example.com", default))
            .ReturnsAsync((User?)null);

        _userRepoMock
            .Setup(r => r.GetByUsernameAsync("octocat", default))
            .ReturnsAsync(existingUser);

        _userRepoMock
            .Setup(r => r.GetByUsernameAsync("octocat1", default))
            .ReturnsAsync((User?)null);

        User? capturedUser = null;
        _userRepoMock
            .Setup(r => r.AddAsync(It.IsAny<User>(), default))
            .Callback<User, CancellationToken>((user, _) => capturedUser = user)
            .ReturnsAsync((User user, CancellationToken _) => user);

        _externalLoginRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ExternalLogin>(), default))
            .ReturnsAsync((ExternalLogin login, CancellationToken _) => login);

        var dto = new ExternalLoginDto(
            Provider: "GitHub",
            ProviderUserId: "55555",
            Username: "octocat",
            Email: "new@example.com");

        var result = await service.ExternalLoginAsync(dto);

        result.IsSuccess.Should().BeTrue();
        capturedUser.Should().NotBeNull();
        capturedUser!.Username.Should().Be("octocat1");
    }

    [Fact]
    public async Task ExternalLoginAsync_ShouldReturnForbidden_WhenLinkedUserIsInactive()
    {
        var service = CreateService();
        var inactiveUser = new User("octocat", "octocat@github.com", BCrypt.Net.BCrypt.HashPassword("random"));
        inactiveUser.Deactivate();
        var existingLogin = new ExternalLogin(inactiveUser.Id, "GitHub", "12345");

        _externalLoginRepoMock
            .Setup(r => r.GetByProviderAsync("GitHub", "12345", default))
            .ReturnsAsync(existingLogin);

        _userRepoMock
            .Setup(r => r.GetByIdAsync(inactiveUser.Id, default))
            .ReturnsAsync(inactiveUser);

        var dto = new ExternalLoginDto(
            Provider: "GitHub",
            ProviderUserId: "12345",
            Username: "octocat",
            Email: "octocat@github.com");

        var result = await service.ExternalLoginAsync(dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ExternalLoginAsync_ShouldCreateNewUser_WhenEmailMatchedUserIsInactive_NoAutoLink()
    {
        // Security: Even if email matches an inactive user, we create a new account
        // rather than auto-linking (which would be a security risk)
        var service = CreateService();
        var inactiveUser = new User("local-user", "inactive@example.com", BCrypt.Net.BCrypt.HashPassword("password"));
        inactiveUser.Deactivate();

        _externalLoginRepoMock
            .Setup(r => r.GetByProviderAsync("GitHub", "77777", default))
            .ReturnsAsync((ExternalLogin?)null);

        _userRepoMock
            .Setup(r => r.GetByEmailAsync("inactive@example.com", default))
            .ReturnsAsync(inactiveUser);

        _userRepoMock
            .Setup(r => r.GetByUsernameAsync("github-user", default))
            .ReturnsAsync((User?)null);

        _userRepoMock
            .Setup(r => r.AddAsync(It.IsAny<User>(), default))
            .ReturnsAsync((User user, CancellationToken _) => user);

        _externalLoginRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ExternalLogin>(), default))
            .ReturnsAsync((ExternalLogin login, CancellationToken _) => login);

        var dto = new ExternalLoginDto(
            Provider: "GitHub",
            ProviderUserId: "77777",
            Username: "github-user",
            Email: "inactive@example.com");

        var result = await service.ExternalLoginAsync(dto);

        // Should succeed by creating a new account with a generated email
        result.IsSuccess.Should().BeTrue();
        _userRepoMock.Verify(r => r.AddAsync(It.IsAny<User>(), default), Times.Once);
    }

    [Theory]
    [InlineData("", "12345", "user", "user@example.com")]
    [InlineData("GitHub", "", "user", "user@example.com")]
    [InlineData("GitHub", "12345", "", "user@example.com")]
    [InlineData("GitHub", "12345", "user", "")]
    public async Task ExternalLoginAsync_ShouldReturnValidationError_WhenRequiredFieldsAreMissing(
        string provider, string providerUserId, string username, string email)
    {
        var service = CreateService();

        var dto = new ExternalLoginDto(provider, providerUserId, username, email);
        var result = await service.ExternalLoginAsync(dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ExternalLoginAsync_ShouldReturnError_WhenJwtConfigIsInvalid()
    {
        var service = CreateService(new JwtSettings
        {
            SecretKey = string.Empty,
            Issuer = "Taskdeck",
            Audience = "TaskdeckUsers",
            ExpirationMinutes = 60
        });

        var dto = new ExternalLoginDto("GitHub", "12345", "user", "user@example.com");
        var result = await service.ExternalLoginAsync(dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnexpectedError);
    }

    private AuthenticationService CreateService(JwtSettings? jwtSettings = null)
    {
        jwtSettings ??= new JwtSettings
        {
            SecretKey = "TaskdeckTestsOnlySecretKeyMustBeLongEnough123!",
            Issuer = "TaskdeckTests",
            Audience = "TaskdeckUsers",
            ExpirationMinutes = 60
        };

        return new AuthenticationService(_unitOfWorkMock.Object, jwtSettings);
    }
}
