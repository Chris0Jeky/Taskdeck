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

public class AccountDeletionServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IHistoryService> _historyServiceMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<INotificationRepository> _notificationRepoMock;
    private readonly Mock<ILlmQueueRepository> _llmQueueRepoMock;
    private readonly Mock<IChatSessionRepository> _chatSessionRepoMock;
    private readonly Mock<IChatMessageRepository> _chatMessageRepoMock;
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock;
    private readonly Mock<IExternalLoginRepository> _externalLoginRepoMock;
    private readonly Mock<IUserPreferenceRepository> _userPrefRepoMock;
    private readonly Mock<INotificationPreferenceRepository> _notifPrefRepoMock;
    private readonly Mock<IBoardAccessRepository> _boardAccessRepoMock;
    private readonly AccountDeletionService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _password = "password123";
    private readonly User _testUser;

    public AccountDeletionServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _historyServiceMock = new Mock<IHistoryService>();
        _userRepoMock = new Mock<IUserRepository>();
        _notificationRepoMock = new Mock<INotificationRepository>();
        _llmQueueRepoMock = new Mock<ILlmQueueRepository>();
        _chatSessionRepoMock = new Mock<IChatSessionRepository>();
        _chatMessageRepoMock = new Mock<IChatMessageRepository>();
        _auditLogRepoMock = new Mock<IAuditLogRepository>();
        _externalLoginRepoMock = new Mock<IExternalLoginRepository>();
        _userPrefRepoMock = new Mock<IUserPreferenceRepository>();
        _notifPrefRepoMock = new Mock<INotificationPreferenceRepository>();
        _boardAccessRepoMock = new Mock<IBoardAccessRepository>();

        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Notifications).Returns(_notificationRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.LlmQueue).Returns(_llmQueueRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.ChatSessions).Returns(_chatSessionRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.ChatMessages).Returns(_chatMessageRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.AuditLogs).Returns(_auditLogRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.ExternalLogins).Returns(_externalLoginRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.UserPreferences).Returns(_userPrefRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.NotificationPreferences).Returns(_notifPrefRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.BoardAccesses).Returns(_boardAccessRepoMock.Object);

        _testUser = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword(_password));

        _historyServiceMock
            .Setup(h => h.LogActionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<AuditAction>(), It.IsAny<Guid?>(), It.IsAny<string?>()))
            .ReturnsAsync(Result.Success());

        _unitOfWorkMock.Setup(u => u.BeginTransactionAsync(default)).Returns(Task.CompletedTask);
        _unitOfWorkMock.Setup(u => u.CommitTransactionAsync(default)).Returns(Task.CompletedTask);
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        _service = new AccountDeletionService(_unitOfWorkMock.Object, _historyServiceMock.Object);
    }

    [Fact]
    public async Task DeleteAccountAsync_Succeeds_WithValidPasswordAndConfirmation()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();

        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeTrue();
        result.Value.Message.Should().Contain("deleted");
    }

    [Fact]
    public async Task DeleteAccountAsync_ReturnsError_WhenPasswordIsWrong()
    {
        // Arrange
        SetupUserFound();
        SetupBoardAccessesNone();
        var request = new AccountDeletionRequest("wrongpassword", "DELETE MY ACCOUNT");

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.AuthenticationFailed);
    }

    [Fact]
    public async Task DeleteAccountAsync_ReturnsError_WhenConfirmationPhraseIsWrong()
    {
        // Arrange
        SetupUserFound();
        var request = new AccountDeletionRequest(_password, "delete my account"); // wrong case

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("DELETE MY ACCOUNT");
    }

    [Fact]
    public async Task DeleteAccountAsync_ReturnsError_WhenConfirmationPhraseIsEmpty()
    {
        // Arrange
        SetupUserFound();
        var request = new AccountDeletionRequest(_password, "");

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task DeleteAccountAsync_ReturnsError_WhenPasswordIsEmpty()
    {
        // Arrange
        var request = new AccountDeletionRequest("", "DELETE MY ACCOUNT");

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task DeleteAccountAsync_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        _userRepoMock.Setup(r => r.GetByIdAsync(_userId, default)).ReturnsAsync((User?)null);
        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task DeleteAccountAsync_ReturnsValidationError_WhenUserIdIsEmpty()
    {
        // Arrange
        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        var result = await _service.DeleteAccountAsync(Guid.Empty, request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task DeleteAccountAsync_ReturnsError_WhenUserIsAlreadyDeactivated()
    {
        // Arrange
        var deactivatedUser = new User("inactive", "inactive@example.com", BCrypt.Net.BCrypt.HashPassword(_password));
        deactivatedUser.Deactivate();
        _userRepoMock.Setup(r => r.GetByIdAsync(_userId, default)).ReturnsAsync(deactivatedUser);
        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        result.ErrorMessage.Should().Contain("already deactivated");
    }

    [Fact]
    public async Task DeleteAccountAsync_ReturnsError_WhenUserIsSoleBoardOwner()
    {
        // Arrange
        SetupUserFound();
        var boardId = Guid.NewGuid();
        var ownerAccess = new BoardAccess(boardId, _userId, UserRole.Owner, _userId);

        _boardAccessRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync(new[] { ownerAccess });
        _boardAccessRepoMock
            .Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new[] { ownerAccess }); // sole owner — no other members

        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        result.ErrorMessage.Should().Contain("sole owner");
    }

    [Fact]
    public async Task DeleteAccountAsync_Succeeds_WhenBoardHasOtherOwner()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();

        var boardId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var ownerAccess = new BoardAccess(boardId, _userId, UserRole.Owner, _userId);
        var otherOwnerAccess = new BoardAccess(boardId, otherUserId, UserRole.Owner, otherUserId);

        _boardAccessRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync(new[] { ownerAccess });
        _boardAccessRepoMock
            .Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new[] { ownerAccess, otherOwnerAccess });

        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAccountAsync_DeletesNotifications()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();

        var notification = new Notification(
            _userId, NotificationType.System, NotificationCadence.Immediate,
            "Test", "Test message");

        _notificationRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, 100000, false, null, default, 0))
            .ReturnsAsync(new[] { notification });

        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.NotificationsDeleted.Should().Be(1);
        _notificationRepoMock.Verify(r => r.DeleteAsync(notification, default), Times.Once);
    }

    [Fact]
    public async Task DeleteAccountAsync_DeletesExternalLogins()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();

        var externalLogin = new ExternalLogin(_userId, "github", "gh-123");
        _externalLoginRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync(new[] { externalLogin });

        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ExternalLoginsDeleted.Should().Be(1);
        _externalLoginRepoMock.Verify(r => r.DeleteAsync(externalLogin, default), Times.Once);
    }

    [Fact]
    public async Task DeleteAccountAsync_DeletesUserPreferences()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();

        var userPref = UserPreference.CreateDefault(_userId);
        _userPrefRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync(userPref);

        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.PreferencesDeleted.Should().BeGreaterOrEqualTo(1);
        _userPrefRepoMock.Verify(r => r.DeleteAsync(userPref, default), Times.Once);
    }

    [Fact]
    public async Task DeleteAccountAsync_DeactivatesUser()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();

        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _userRepoMock.Verify(r => r.UpdateAsync(It.Is<User>(u => !u.IsActive), default), Times.Once);
    }

    [Fact]
    public async Task DeleteAccountAsync_AnonymizesUserPII()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();

        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _userRepoMock.Verify(r => r.UpdateAsync(
            It.Is<User>(u =>
                u.Username.StartsWith("deleted-") &&
                u.Email.Contains("@anonymized.local") &&
                !u.IsActive),
            default), Times.Once);
    }

    [Fact]
    public async Task DeleteAccountAsync_AnonymizedSuffix_IsNotDerivedFromUserId()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();

        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert — the anonymized suffix must NOT be the first chars of the user ID
        result.IsSuccess.Should().BeTrue();
        var userIdPrefix = _userId.ToString("N")[..12];
        _userRepoMock.Verify(r => r.UpdateAsync(
            It.Is<User>(u =>
                u.Username.StartsWith("deleted-") &&
                !u.Username.Contains(userIdPrefix)),
            default), Times.Once);
    }

    [Fact]
    public async Task DeleteAccountAsync_DeletesBoardAccessRecords()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();

        var boardId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var memberAccess = new BoardAccess(boardId, _userId, UserRole.Admin, otherUserId);
        var otherOwnerAccess = new BoardAccess(boardId, otherUserId, UserRole.Owner, otherUserId);

        _boardAccessRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync(new[] { memberAccess });
        // No sole-owner check needed since user is Admin, not Owner

        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _boardAccessRepoMock.Verify(r => r.DeleteAsync(memberAccess, default), Times.Once);
    }

    [Fact]
    public async Task DeleteAccountAsync_UsesTransaction()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();
        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        await _service.DeleteAccountAsync(_userId, request);

        // Assert
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(default), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(default), Times.Once);
    }

    [Fact]
    public async Task DeleteAccountAsync_RollsBackOnError()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();

        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(default)).ThrowsAsync(new Exception("DB error"));

        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnexpectedError);
        result.ErrorMessage.Should().NotContain("DB error"); // must not leak internal details
        _unitOfWorkMock.Verify(u => u.RollbackTransactionAsync(default), Times.Once);
    }

    [Fact]
    public async Task DeleteAccountAsync_LogsDeletionRequest_InsideTransaction()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();
        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        await _service.DeleteAccountAsync(_userId, request);

        // Assert — the request log should happen inside the transaction
        _historyServiceMock.Verify(
            h => h.LogActionAsync("User", _userId, AuditAction.AccountDeletionRequested, _userId, It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteAccountAsync_LogsAnonymizationCompletion_WithoutPII()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();
        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        await _service.DeleteAccountAsync(_userId, request);

        // Assert — completion log uses null userId to avoid linking to deleted account
        _historyServiceMock.Verify(
            h => h.LogActionAsync("User", _userId, AuditAction.AccountAnonymized, null, It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteAccountAsync_AuditLogs_DoNotContainPII()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();
        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        await _service.DeleteAccountAsync(_userId, request);

        // Assert — audit log changes parameter should never contain user email, username, or password
        _historyServiceMock.Verify(
            h => h.LogActionAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<AuditAction>(),
                It.IsAny<Guid?>(),
                It.Is<string?>(s => s == null || (!s.Contains("test@example.com") && !s.Contains("testuser") && !s.Contains(_password)))),
            Times.Exactly(2));
    }

    [Fact]
    public async Task DeleteAccountAsync_InvalidatesActiveUserCache()
    {
        // Arrange — create a service with a cache mock
        var cacheMock = new Mock<IActiveUserCache>();
        var serviceWithCache = new AccountDeletionService(
            _unitOfWorkMock.Object, _historyServiceMock.Object, cacheMock.Object);

        SetupUserFound();
        SetupEmptyRepositories();
        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        var result = await serviceWithCache.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        cacheMock.Verify(c => c.Invalidate(_userId), Times.Once);
    }

    [Fact]
    public async Task DeleteAccountAsync_SucceedsWithoutCache()
    {
        // Arrange — the default _service has no cache (null), should still work
        SetupUserFound();
        SetupEmptyRepositories();
        var request = new AccountDeletionRequest(_password, "DELETE MY ACCOUNT");

        // Act
        var result = await _service.DeleteAccountAsync(_userId, request);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    private void SetupUserFound()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(_userId, default)).ReturnsAsync(_testUser);
    }

    private void SetupBoardAccessesNone()
    {
        _boardAccessRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync(Enumerable.Empty<BoardAccess>());
    }

    private void SetupEmptyRepositories()
    {
        SetupBoardAccessesNone();
        _auditLogRepoMock
            .Setup(r => r.GetByUserAsync(_userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<AuditLog>());
        _notificationRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<int>(), false, null, default, 0))
            .ReturnsAsync(Enumerable.Empty<Notification>());
        _llmQueueRepoMock
            .Setup(r => r.GetByUserAsync(_userId, default))
            .ReturnsAsync(Enumerable.Empty<LlmRequest>());
        _chatSessionRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<ChatSession>());
        _externalLoginRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync(Enumerable.Empty<ExternalLogin>());
        _userPrefRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync((UserPreference?)null);
        _notifPrefRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync((NotificationPreference?)null);
    }
}
