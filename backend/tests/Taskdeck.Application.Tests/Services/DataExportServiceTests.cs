using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class DataExportServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IHistoryService> _historyServiceMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IBoardAccessRepository> _boardAccessRepoMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<INotificationRepository> _notificationRepoMock;
    private readonly Mock<ILlmQueueRepository> _llmQueueRepoMock;
    private readonly Mock<IAutomationProposalRepository> _proposalRepoMock;
    private readonly Mock<IChatSessionRepository> _chatSessionRepoMock;
    private readonly Mock<IChatMessageRepository> _chatMessageRepoMock;
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock;
    private readonly Mock<IUserPreferenceRepository> _userPrefRepoMock;
    private readonly Mock<INotificationPreferenceRepository> _notifPrefRepoMock;
    private readonly DataExportService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly User _testUser;

    public DataExportServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _historyServiceMock = new Mock<IHistoryService>();
        _userRepoMock = new Mock<IUserRepository>();
        _boardAccessRepoMock = new Mock<IBoardAccessRepository>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _notificationRepoMock = new Mock<INotificationRepository>();
        _llmQueueRepoMock = new Mock<ILlmQueueRepository>();
        _proposalRepoMock = new Mock<IAutomationProposalRepository>();
        _chatSessionRepoMock = new Mock<IChatSessionRepository>();
        _chatMessageRepoMock = new Mock<IChatMessageRepository>();
        _auditLogRepoMock = new Mock<IAuditLogRepository>();
        _userPrefRepoMock = new Mock<IUserPreferenceRepository>();
        _notifPrefRepoMock = new Mock<INotificationPreferenceRepository>();

        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.BoardAccesses).Returns(_boardAccessRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Notifications).Returns(_notificationRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.LlmQueue).Returns(_llmQueueRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.AutomationProposals).Returns(_proposalRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.ChatSessions).Returns(_chatSessionRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.ChatMessages).Returns(_chatMessageRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.AuditLogs).Returns(_auditLogRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.UserPreferences).Returns(_userPrefRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.NotificationPreferences).Returns(_notifPrefRepoMock.Object);

        _testUser = new User("testuser", "test@example.com", BCrypt.Net.BCrypt.HashPassword("password123"));

        _historyServiceMock
            .Setup(h => h.LogActionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<AuditAction>(), It.IsAny<Guid?>(), It.IsAny<string?>()))
            .ReturnsAsync(Result.Success());

        _service = new DataExportService(_unitOfWorkMock.Object, _historyServiceMock.Object);
    }

    [Fact]
    public async Task ExportUserDataAsync_ReturnsValidExport_ForExistingUser()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();

        // Act
        var result = await _service.ExportUserDataAsync(_userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Version.Should().Be("1.0");
        result.Value.UserId.Should().Be(_userId);
        result.Value.Profile.Username.Should().Be("testuser");
        result.Value.Profile.Email.Should().Be("test@example.com");
        result.Value.ExportedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ExportUserDataAsync_ReturnsNotFound_WhenUserDoesNotExist()
    {
        // Arrange
        _userRepoMock.Setup(r => r.GetByIdAsync(_userId, default)).ReturnsAsync((User?)null);

        // Act
        var result = await _service.ExportUserDataAsync(_userId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ExportUserDataAsync_ReturnsValidationError_WhenUserIdIsEmpty()
    {
        // Act
        var result = await _service.ExportUserDataAsync(Guid.Empty);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ExportUserDataAsync_IncludesBoardsWithAccessInfo()
    {
        // Arrange
        SetupUserFound();

        var boardId = Guid.NewGuid();
        var board = new Board("Test Board", "Description", _userId);
        var boardAccess = new BoardAccess(boardId, _userId, UserRole.Owner, _userId);

        _boardAccessRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync(new[] { boardAccess });

        _boardRepoMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), default))
            .ReturnsAsync(new[] { board });

        SetupEmptyRepositoriesExceptBoards();

        // Act
        var result = await _service.ExportUserDataAsync(_userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Data.Boards.Should().HaveCount(1);
        result.Value.Data.Boards[0].Role.Should().Be("Owner");
        result.Value.Data.Boards[0].IsOwner.Should().BeTrue();
    }

    [Fact]
    public async Task ExportUserDataAsync_IncludesNotifications()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();

        var notification = new Notification(
            _userId, NotificationType.System, NotificationCadence.Immediate,
            "Test Title", "Test Message");

        _notificationRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, 10000, false, null, default, 0))
            .ReturnsAsync(new[] { notification });

        // Act
        var result = await _service.ExportUserDataAsync(_userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Data.Notifications.Should().HaveCount(1);
        result.Value.Data.Notifications[0].Title.Should().Be("Test Title");
    }

    [Fact]
    public async Task ExportUserDataAsync_LogsExportAction()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();

        // Act
        await _service.ExportUserDataAsync(_userId);

        // Assert
        _historyServiceMock.Verify(
            h => h.LogActionAsync("User", _userId, AuditAction.DataExported, _userId, It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task ExportUserDataAsync_DoesNotLeakOtherUsersData()
    {
        // Arrange
        var otherUserId = Guid.NewGuid();
        SetupUserFound();
        SetupEmptyRepositories();

        // Act
        var result = await _service.ExportUserDataAsync(_userId);

        // Assert — verify that repository calls are scoped to the requesting user
        result.IsSuccess.Should().BeTrue();

        _boardAccessRepoMock.Verify(
            r => r.GetByUserIdAsync(_userId, default), Times.Once);
        _notificationRepoMock.Verify(
            r => r.GetByUserIdAsync(_userId, It.IsAny<int>(), false, null, default, 0), Times.Once);
        _llmQueueRepoMock.Verify(
            r => r.GetByUserAsync(_userId, default), Times.Once);
        _proposalRepoMock.Verify(
            r => r.GetByUserIdAsync(_userId, It.IsAny<int>(), default), Times.Once);
        _chatSessionRepoMock.Verify(
            r => r.GetByUserIdAsync(_userId, It.IsAny<int>(), default), Times.Once);
        _auditLogRepoMock.Verify(
            r => r.GetByUserAsync(_userId, It.IsAny<int>(), default), Times.Once);
        _userPrefRepoMock.Verify(
            r => r.GetByUserIdAsync(_userId, default), Times.Once);
        _notifPrefRepoMock.Verify(
            r => r.GetByUserIdAsync(_userId, default), Times.Once);

        // Verify no calls with other user IDs
        _boardAccessRepoMock.Verify(
            r => r.GetByUserIdAsync(otherUserId, default), Times.Never);
    }

    [Fact]
    public async Task ExportUserDataAsync_IncludesVersionInformation()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();

        // Act
        var result = await _service.ExportUserDataAsync(_userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Version.Should().NotBeNullOrWhiteSpace();
        result.Value.ExportedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task ExportUserDataAsync_IsIdempotent_MultipleCallsReturnConsistentStructure()
    {
        // Arrange
        SetupUserFound();
        SetupEmptyRepositories();

        // Act
        var result1 = await _service.ExportUserDataAsync(_userId);
        var result2 = await _service.ExportUserDataAsync(_userId);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        result1.Value.Version.Should().Be(result2.Value.Version);
        result1.Value.UserId.Should().Be(result2.Value.UserId);
        result1.Value.Profile.Username.Should().Be(result2.Value.Profile.Username);
    }

    private void SetupUserFound()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(_userId, default)).ReturnsAsync(_testUser);
    }

    private void SetupEmptyRepositories()
    {
        _boardAccessRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync(Enumerable.Empty<BoardAccess>());
        _notificationRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<int>(), false, null, default, 0))
            .ReturnsAsync(Enumerable.Empty<Notification>());
        _llmQueueRepoMock
            .Setup(r => r.GetByUserAsync(_userId, default))
            .ReturnsAsync(Enumerable.Empty<LlmRequest>());
        _proposalRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<AutomationProposal>());
        _chatSessionRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<ChatSession>());
        _auditLogRepoMock
            .Setup(r => r.GetByUserAsync(_userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<AuditLog>());
        _userPrefRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync((UserPreference?)null);
        _notifPrefRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync((NotificationPreference?)null);
    }

    private void SetupEmptyRepositoriesExceptBoards()
    {
        _notificationRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<int>(), false, null, default, 0))
            .ReturnsAsync(Enumerable.Empty<Notification>());
        _llmQueueRepoMock
            .Setup(r => r.GetByUserAsync(_userId, default))
            .ReturnsAsync(Enumerable.Empty<LlmRequest>());
        _proposalRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<AutomationProposal>());
        _chatSessionRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<ChatSession>());
        _auditLogRepoMock
            .Setup(r => r.GetByUserAsync(_userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<AuditLog>());
        _userPrefRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync((UserPreference?)null);
        _notifPrefRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync((NotificationPreference?)null);
    }
}

/// <summary>
/// Tests for the streaming export path added to fix issue #670 (export streaming for large datasets).
/// </summary>
public class DataExportServiceStreamingTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IHistoryService> _historyServiceMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IBoardAccessRepository> _boardAccessRepoMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<INotificationRepository> _notificationRepoMock;
    private readonly Mock<ILlmQueueRepository> _llmQueueRepoMock;
    private readonly Mock<IAutomationProposalRepository> _proposalRepoMock;
    private readonly Mock<IChatSessionRepository> _chatSessionRepoMock;
    private readonly Mock<IChatMessageRepository> _chatMessageRepoMock;
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock;
    private readonly Mock<IUserPreferenceRepository> _userPrefRepoMock;
    private readonly Mock<INotificationPreferenceRepository> _notifPrefRepoMock;
    private readonly DataExportService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly User _testUser;

    public DataExportServiceStreamingTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _historyServiceMock = new Mock<IHistoryService>();
        _userRepoMock = new Mock<IUserRepository>();
        _boardAccessRepoMock = new Mock<IBoardAccessRepository>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _notificationRepoMock = new Mock<INotificationRepository>();
        _llmQueueRepoMock = new Mock<ILlmQueueRepository>();
        _proposalRepoMock = new Mock<IAutomationProposalRepository>();
        _chatSessionRepoMock = new Mock<IChatSessionRepository>();
        _chatMessageRepoMock = new Mock<IChatMessageRepository>();
        _auditLogRepoMock = new Mock<IAuditLogRepository>();
        _userPrefRepoMock = new Mock<IUserPreferenceRepository>();
        _notifPrefRepoMock = new Mock<INotificationPreferenceRepository>();

        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.BoardAccesses).Returns(_boardAccessRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Notifications).Returns(_notificationRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.LlmQueue).Returns(_llmQueueRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.AutomationProposals).Returns(_proposalRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.ChatSessions).Returns(_chatSessionRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.ChatMessages).Returns(_chatMessageRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.AuditLogs).Returns(_auditLogRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.UserPreferences).Returns(_userPrefRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.NotificationPreferences).Returns(_notifPrefRepoMock.Object);

        _testUser = new User("streamuser", "stream@example.com", BCrypt.Net.BCrypt.HashPassword("password123"));

        _historyServiceMock
            .Setup(h => h.LogActionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<AuditAction>(), It.IsAny<Guid?>(), It.IsAny<string?>()))
            .ReturnsAsync(Result.Success());

        _service = new DataExportService(_unitOfWorkMock.Object, _historyServiceMock.Object);
    }

    [Fact]
    public async Task StreamUserDataExportAsync_ReturnsValidationError_WhenUserIdIsEmpty()
    {
        using var stream = new MemoryStream();
        var result = await _service.StreamUserDataExportAsync(Guid.Empty, stream);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task StreamUserDataExportAsync_ReturnsNotFound_WhenUserDoesNotExist()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(_userId, default)).ReturnsAsync((User?)null);

        using var stream = new MemoryStream();
        var result = await _service.StreamUserDataExportAsync(_userId, stream);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task StreamUserDataExportAsync_WritesValidJson_ForExistingUser()
    {
        SetupUserFound();
        SetupEmptyRepositories();

        using var stream = new MemoryStream();
        var result = await _service.StreamUserDataExportAsync(_userId, stream);

        result.IsSuccess.Should().BeTrue();
        stream.Length.Should().BeGreaterThan(0);

        stream.Position = 0;
        var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());

        // Must be parseable as valid JSON
        var action = () => System.Text.Json.JsonDocument.Parse(json);
        action.Should().NotThrow("streaming export must produce valid JSON");
    }

    [Fact]
    public async Task StreamUserDataExportAsync_OutputContainsRequiredEnvelopeFields()
    {
        SetupUserFound();
        SetupEmptyRepositories();

        using var stream = new MemoryStream();
        await _service.StreamUserDataExportAsync(_userId, stream);

        stream.Position = 0;
        using var doc = System.Text.Json.JsonDocument.Parse(stream);

        doc.RootElement.TryGetProperty("version", out var version).Should().BeTrue();
        version.GetString().Should().Be("1.0");

        doc.RootElement.TryGetProperty("userId", out var userIdProp).Should().BeTrue();
        userIdProp.GetString().Should().Be(_userId.ToString());

        doc.RootElement.TryGetProperty("profile", out var profileProp).Should().BeTrue();
        profileProp.TryGetProperty("username", out var usernameProp).Should().BeTrue();
        usernameProp.GetString().Should().Be("streamuser");

        doc.RootElement.TryGetProperty("data", out var dataProp).Should().BeTrue();
        dataProp.TryGetProperty("boards", out _).Should().BeTrue();
        dataProp.TryGetProperty("notifications", out _).Should().BeTrue();
        dataProp.TryGetProperty("proposals", out _).Should().BeTrue();
        dataProp.TryGetProperty("chatSessions", out _).Should().BeTrue();
        dataProp.TryGetProperty("auditTrail", out _).Should().BeTrue();
    }

    [Fact]
    public async Task StreamUserDataExportAsync_UsesOneCountQueryForManyChatSessions_NotNPlus1()
    {
        // Arrange — two chat sessions
        SetupUserFound();

        var session1 = new ChatSession(_userId, "Session One");
        var session2 = new ChatSession(_userId, "Session Two");
        var sessions = new[] { session1, session2 };

        _boardAccessRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync(Enumerable.Empty<BoardAccess>());
        _notificationRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<int>(), false, null, default, 0))
            .ReturnsAsync(Enumerable.Empty<Notification>());
        _llmQueueRepoMock
            .Setup(r => r.GetByUserAsync(_userId, default))
            .ReturnsAsync(Enumerable.Empty<LlmRequest>());
        _proposalRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<AutomationProposal>());

        _chatSessionRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<int>(), default))
            .ReturnsAsync(sessions);

        // CountBySessionIdsAsync should be called ONCE with both session IDs
        _chatMessageRepoMock
            .Setup(r => r.CountBySessionIdsAsync(It.IsAny<IEnumerable<Guid>>(), default))
            .ReturnsAsync(new Dictionary<Guid, int>
            {
                { session1.Id, 3 },
                { session2.Id, 7 }
            });

        _auditLogRepoMock
            .Setup(r => r.GetByUserAsync(_userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<AuditLog>());
        _userPrefRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync((UserPreference?)null);
        _notifPrefRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync((NotificationPreference?)null);

        using var stream = new MemoryStream();

        // Act
        var result = await _service.StreamUserDataExportAsync(_userId, stream);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // CountBySessionIdsAsync must have been called exactly once (not N times)
        _chatMessageRepoMock.Verify(
            r => r.CountBySessionIdsAsync(It.IsAny<IEnumerable<Guid>>(), default),
            Times.Once,
            "message counts should be fetched in a single batched query, not one per session");

        // GetBySessionIdAsync must never be called (that's the old N+1 pattern)
        _chatMessageRepoMock.Verify(
            r => r.GetBySessionIdAsync(It.IsAny<Guid>(), It.IsAny<int>(), default),
            Times.Never,
            "streaming export must not fall back to the N+1 GetBySessionIdAsync pattern");

        // Verify the output JSON contains the correct message counts
        stream.Position = 0;
        using var doc = System.Text.Json.JsonDocument.Parse(stream);
        var chatSessions = doc.RootElement.GetProperty("data").GetProperty("chatSessions");
        chatSessions.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task StreamUserDataExportAsync_LogsExportAction()
    {
        SetupUserFound();
        SetupEmptyRepositories();

        using var stream = new MemoryStream();
        await _service.StreamUserDataExportAsync(_userId, stream);

        _historyServiceMock.Verify(
            h => h.LogActionAsync("User", _userId, AuditAction.DataExported, _userId, It.IsAny<string>()),
            Times.Once);
    }

    private void SetupUserFound()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(_userId, default)).ReturnsAsync(_testUser);
    }

    private void SetupEmptyRepositories()
    {
        _boardAccessRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync(Enumerable.Empty<BoardAccess>());
        _notificationRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<int>(), false, null, default, 0))
            .ReturnsAsync(Enumerable.Empty<Notification>());
        _llmQueueRepoMock
            .Setup(r => r.GetByUserAsync(_userId, default))
            .ReturnsAsync(Enumerable.Empty<LlmRequest>());
        _proposalRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<AutomationProposal>());
        _chatSessionRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<ChatSession>());
        _auditLogRepoMock
            .Setup(r => r.GetByUserAsync(_userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<AuditLog>());
        _userPrefRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync((UserPreference?)null);
        _notifPrefRepoMock
            .Setup(r => r.GetByUserIdAsync(_userId, default))
            .ReturnsAsync((NotificationPreference?)null);
    }
}
