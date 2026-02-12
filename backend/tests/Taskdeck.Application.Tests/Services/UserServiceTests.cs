using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class UserServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly UserService _service;

    public UserServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _userRepoMock = new Mock<IUserRepository>();

        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);

        _service = new UserService(_unitOfWorkMock.Object);
    }

    #region CreateUserAsync Tests

    [Fact]
    public async Task CreateUserAsync_ShouldReturnSuccess_WithValidData()
    {
        // Arrange
        var dto = new CreateUserDto("testuser", "test@example.com", "password123");

        _userRepoMock.Setup(r => r.ExistsAsync(dto.Username, dto.Email, default))
            .ReturnsAsync(false);
        _userRepoMock.Setup(r => r.AddAsync(It.IsAny<User>(), default))
            .ReturnsAsync((User u, CancellationToken ct) => u);

        // Act
        var result = await _service.CreateUserAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Username.Should().Be("testuser");
        result.Value.Email.Should().Be("test@example.com");
        result.Value.DefaultRole.Should().Be(UserRole.Editor);
        result.Value.IsActive.Should().BeTrue();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task CreateUserAsync_ShouldReturnConflict_WhenUsernameOrEmailAlreadyExists()
    {
        // Arrange
        var dto = new CreateUserDto("existinguser", "existing@example.com", "password123");

        _userRepoMock.Setup(r => r.ExistsAsync(dto.Username, dto.Email, default))
            .ReturnsAsync(true);

        // Act
        var result = await _service.CreateUserAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task CreateUserAsync_ShouldReturnValidationError_WhenUsernameIsEmpty()
    {
        // Arrange
        var dto = new CreateUserDto("", "test@example.com", "password123");

        _userRepoMock.Setup(r => r.ExistsAsync(dto.Username, dto.Email, default))
            .ReturnsAsync(false);

        // Act
        var result = await _service.CreateUserAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    #endregion

    #region GetUserByIdAsync Tests

    [Fact]
    public async Task GetUserByIdAsync_ShouldReturnUser_WhenExists()
    {
        // Arrange
        var user = new User("testuser", "test@example.com", "hashedpassword");

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default))
            .ReturnsAsync(user);

        // Act
        var result = await _service.GetUserByIdAsync(user.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(user.Id);
        result.Value.Username.Should().Be("testuser");
    }

    [Fact]
    public async Task GetUserByIdAsync_ShouldReturnNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _service.GetUserByIdAsync(userId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion

    #region GetUserByUsernameAsync Tests

    [Fact]
    public async Task GetUserByUsernameAsync_ShouldReturnUser_WhenExists()
    {
        // Arrange
        var user = new User("testuser", "test@example.com", "hashedpassword");

        _userRepoMock.Setup(r => r.GetByUsernameAsync("testuser", default))
            .ReturnsAsync(user);

        // Act
        var result = await _service.GetUserByUsernameAsync("testuser");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Username.Should().Be("testuser");
    }

    [Fact]
    public async Task GetUserByUsernameAsync_ShouldReturnNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        _userRepoMock.Setup(r => r.GetByUsernameAsync("nonexistent", default))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _service.GetUserByUsernameAsync("nonexistent");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion

    #region GetUserByEmailAsync Tests

    [Fact]
    public async Task GetUserByEmailAsync_ShouldReturnUser_WhenExists()
    {
        // Arrange
        var user = new User("testuser", "test@example.com", "hashedpassword");

        _userRepoMock.Setup(r => r.GetByEmailAsync("test@example.com", default))
            .ReturnsAsync(user);

        // Act
        var result = await _service.GetUserByEmailAsync("test@example.com");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be("test@example.com");
    }

    [Fact]
    public async Task GetUserByEmailAsync_ShouldReturnNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        _userRepoMock.Setup(r => r.GetByEmailAsync("notfound@example.com", default))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _service.GetUserByEmailAsync("notfound@example.com");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion

    #region UpdateUserAsync Tests

    [Fact]
    public async Task UpdateUserAsync_ShouldUpdateUsername()
    {
        // Arrange
        var user = new User("olduser", "test@example.com", "hashedpassword");
        var dto = new UpdateUserDto(Username: "newuser");

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default))
            .ReturnsAsync(user);

        // Act
        var result = await _service.UpdateUserAsync(user.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Username.Should().Be("newuser");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldReturnNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var dto = new UpdateUserDto(Username: "newuser");

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _service.UpdateUserAsync(userId, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Contain("User");
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldReturnValidationError_WhenUsernameIsInvalid()
    {
        // Arrange
        var user = new User("testuser", "test@example.com", "hashedpassword");
        var dto = new UpdateUserDto(Username: "ab"); // Too short, must be at least 3 chars

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default))
            .ReturnsAsync(user);

        // Act
        var result = await _service.UpdateUserAsync(user.Id, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldReturnConflict_WhenUsernameBelongsToAnotherUser()
    {
        var user = new User("currentuser", "current@example.com", "hashedpassword");
        var existing = new User("takenuser", "taken@example.com", "hashedpassword");
        var dto = new UpdateUserDto(Username: existing.Username);

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.GetByUsernameAsync(existing.Username, default)).ReturnsAsync(existing);

        var result = await _service.UpdateUserAsync(user.Id, dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldReturnConflict_WhenEmailBelongsToAnotherUser()
    {
        var user = new User("currentuser", "current@example.com", "hashedpassword");
        var existing = new User("takenuser", "taken@example.com", "hashedpassword");
        var dto = new UpdateUserDto(Email: existing.Email);

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.GetByEmailAsync(existing.Email, default)).ReturnsAsync(existing);

        var result = await _service.UpdateUserAsync(user.Id, dto);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    #endregion

    #region DeactivateUserAsync Tests

    [Fact]
    public async Task DeactivateUserAsync_ShouldDeactivateUser_WhenExists()
    {
        // Arrange
        var user = new User("testuser", "test@example.com", "hashedpassword");
        user.IsActive.Should().BeTrue(); // Confirm initially active

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default))
            .ReturnsAsync(user);

        // Act
        var result = await _service.DeactivateUserAsync(user.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        user.IsActive.Should().BeFalse();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task DeactivateUserAsync_ShouldReturnNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _service.DeactivateUserAsync(userId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    #endregion

    #region ActivateUserAsync Tests

    [Fact]
    public async Task ActivateUserAsync_ShouldActivateUser_WhenExists()
    {
        // Arrange
        var user = new User("testuser", "test@example.com", "hashedpassword");
        user.Deactivate();
        user.IsActive.Should().BeFalse(); // Confirm deactivated

        _userRepoMock.Setup(r => r.GetByIdAsync(user.Id, default))
            .ReturnsAsync(user);

        // Act
        var result = await _service.ActivateUserAsync(user.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        user.IsActive.Should().BeTrue();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task ActivateUserAsync_ShouldReturnNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _service.ActivateUserAsync(userId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    #endregion

    #region ListUsersAsync Tests

    [Fact]
    public async Task ListUsersAsync_ShouldReturnAllUsers()
    {
        // Arrange
        var users = new List<User>
        {
            new User("user1", "user1@example.com", "hashedpassword"),
            new User("user2", "user2@example.com", "hashedpassword")
        };

        _userRepoMock.Setup(r => r.GetAllAsync(default))
            .ReturnsAsync(users);

        // Act
        var result = await _service.ListUsersAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    #endregion
}
