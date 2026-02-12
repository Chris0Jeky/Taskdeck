using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class AuthenticationServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IUserRepository> _userRepoMock;

    public AuthenticationServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _userRepoMock = new Mock<IUserRepository>();

        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnToken_WhenCredentialsAreValid()
    {
        var password = "password123";
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword(password));
        var service = CreateService();

        _userRepoMock.Setup(r => r.GetByUsernameAsync(user.Username, default)).ReturnsAsync(user);

        var result = await service.LoginAsync(new LoginDto(user.Username, password));

        result.IsSuccess.Should().BeTrue();
        result.Value.Token.Should().NotBeNullOrWhiteSpace();
        result.Value.User.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task LoginAsync_ShouldAuthenticateEmailOwner_WhenUsernameEmailCollisionExists()
    {
        const string collision = "collision@example.com";
        var usernameOwner = new User(collision, "owner1@example.com", BCrypt.Net.BCrypt.HashPassword("password-a"));
        var emailOwner = new User("email_owner", collision, BCrypt.Net.BCrypt.HashPassword("password-b"));
        var service = CreateService();

        _userRepoMock.Setup(r => r.GetByUsernameAsync(collision, default)).ReturnsAsync(usernameOwner);
        _userRepoMock.Setup(r => r.GetByEmailAsync(collision, default)).ReturnsAsync(emailOwner);

        var result = await service.LoginAsync(new LoginDto(collision, "password-b"));

        result.IsSuccess.Should().BeTrue();
        result.Value.User.Id.Should().Be(emailOwner.Id);
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnUnexpectedError_WhenJwtConfigIsInvalid()
    {
        var service = CreateService(new JwtSettings
        {
            SecretKey = string.Empty,
            Issuer = "Taskdeck",
            Audience = "TaskdeckUsers",
            ExpirationMinutes = 60
        });

        var result = await service.LoginAsync(new LoginDto("someone", "password"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnexpectedError);
        _userRepoMock.Verify(r => r.GetByUsernameAsync(It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_ShouldReturnConflict_WhenUserAlreadyExists()
    {
        var service = CreateService();
        var dto = new CreateUserDto("existing", "existing@example.com", "password123");

        _userRepoMock.Setup(r => r.ExistsAsync(dto.Username, dto.Email, default)).ReturnsAsync(true);

        var result = await service.RegisterAsync(dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        _userRepoMock.Verify(r => r.AddAsync(It.IsAny<User>(), default), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_ShouldNotCreateUser_WhenJwtConfigIsInvalid()
    {
        var service = CreateService(new JwtSettings
        {
            SecretKey = "short-secret",
            Issuer = "Taskdeck",
            Audience = "TaskdeckUsers",
            ExpirationMinutes = 60
        });

        var result = await service.RegisterAsync(new CreateUserDto("newuser", "newuser@example.com", "password123"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnexpectedError);
        _userRepoMock.Verify(r => r.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
        _userRepoMock.Verify(r => r.AddAsync(It.IsAny<User>(), default), Times.Never);
    }

    [Fact]
    public async Task ChangePasswordAsync_ShouldReturnForbidden_WhenUserIsInactive()
    {
        var service = CreateService();
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("password123"));
        user.Deactivate();

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var result = await service.ChangePasswordAsync(user.Id, "password123", "newpassword123");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ValidateTokenAsync_ShouldReturnUser_WhenTokenIsValid()
    {
        var service = CreateService();
        var password = "password123";
        var user = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword(password));

        _userRepoMock.Setup(r => r.GetByUsernameAsync(user.Username, default)).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);

        var loginResult = await service.LoginAsync(new LoginDto(user.Username, password));
        var validationResult = await service.ValidateTokenAsync(loginResult.Value.Token);

        validationResult.IsSuccess.Should().BeTrue();
        validationResult.Value.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task ValidateTokenAsync_ShouldReturnUnauthorized_WhenTokenIsInvalid()
    {
        var service = CreateService();

        var result = await service.ValidateTokenAsync("invalid-token");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
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
