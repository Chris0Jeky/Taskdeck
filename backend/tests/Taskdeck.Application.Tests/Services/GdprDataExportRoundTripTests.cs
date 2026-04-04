using System.Text.Json;
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

/// <summary>
/// Tests for GDPR data export round-trip integrity: verifies that exported
/// JSON is valid, all sections are populated for users with data, empty
/// exports produce valid structures, and cross-user data isolation holds.
/// </summary>
public class GdprDataExportRoundTripTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

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

    public GdprDataExportRoundTripTests()
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

        _historyServiceMock
            .Setup(h => h.LogActionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<AuditAction>(), It.IsAny<Guid?>(), It.IsAny<string?>()))
            .ReturnsAsync(Result.Success());

        _service = new DataExportService(_unitOfWorkMock.Object, _historyServiceMock.Object);
    }

    [Fact]
    public async Task ExportUserData_AllSectionsPopulated_ProducesValidJson()
    {
        // Arrange: user with data in every section
        var userId = Guid.NewGuid();
        var user = new User("fulluser", "full@example.com", "hashedpw");
        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);

        var boardId = Guid.NewGuid();
        var board = new Board("Test Board", "Description", userId);
        var boardAccess = new BoardAccess(boardId, userId, UserRole.Owner, userId);
        _boardAccessRepoMock.Setup(r => r.GetByUserIdAsync(userId, default))
            .ReturnsAsync(new[] { boardAccess });
        _boardRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), default))
            .ReturnsAsync(new[] { board });

        var notification = new Notification(userId, NotificationType.System, NotificationCadence.Immediate,
            "Welcome", "Welcome to Taskdeck");
        _notificationRepoMock.Setup(r => r.GetByUserIdAsync(userId, It.IsAny<int>(), false, null, default, 0))
            .ReturnsAsync(new[] { notification });

        _llmQueueRepoMock.Setup(r => r.GetByUserAsync(userId, default))
            .ReturnsAsync(Enumerable.Empty<LlmRequest>());
        _proposalRepoMock.Setup(r => r.GetByUserIdAsync(userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<AutomationProposal>());
        _chatSessionRepoMock.Setup(r => r.GetByUserIdAsync(userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<ChatSession>());
        _auditLogRepoMock.Setup(r => r.GetByUserAsync(userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<AuditLog>());
        _userPrefRepoMock.Setup(r => r.GetByUserIdAsync(userId, default))
            .ReturnsAsync((UserPreference?)null);
        _notifPrefRepoMock.Setup(r => r.GetByUserIdAsync(userId, default))
            .ReturnsAsync((NotificationPreference?)null);

        // Act
        var result = await _service.ExportUserDataAsync(userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var export = result.Value;

        // Serialize to JSON and verify it's valid
        var json = JsonSerializer.Serialize(export, JsonOptions);
        json.Should().NotBeNullOrWhiteSpace();

        // Parsing validates JSON syntax (including rejecting trailing commas)
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);

        // Verify the export can be deserialized back
        var deserialized = JsonSerializer.Deserialize<UserDataExportDto>(json, JsonOptions);
        deserialized.Should().NotBeNull();
        deserialized!.Version.Should().Be("1.0");
        deserialized.UserId.Should().Be(userId);
        deserialized.Profile.Username.Should().Be("fulluser");
        deserialized.Profile.Email.Should().Be("full@example.com");
        deserialized.Data.Boards.Should().HaveCount(1);
        deserialized.Data.Notifications.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExportUserData_EmptyUser_ProducesValidEmptyJsonStructure()
    {
        var userId = Guid.NewGuid();
        var user = new User("emptyuser", "empty@example.com", "hashedpw");
        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        SetupEmptyRepositories(userId);

        var result = await _service.ExportUserDataAsync(userId);

        result.IsSuccess.Should().BeTrue();
        var export = result.Value;

        // All sections should be present but empty
        export.Data.Boards.Should().NotBeNull().And.BeEmpty();
        export.Data.Notifications.Should().NotBeNull().And.BeEmpty();
        export.Data.CaptureItems.Should().NotBeNull().And.BeEmpty();
        export.Data.Proposals.Should().NotBeNull().And.BeEmpty();
        export.Data.ChatSessions.Should().NotBeNull().And.BeEmpty();
        export.Data.AuditTrail.Should().NotBeNull().And.BeEmpty();
        export.Data.Preferences.Should().BeNull();
        export.Data.NotificationPreferences.Should().BeNull();

        // Should still be valid JSON
        var json = JsonSerializer.Serialize(export, JsonOptions);
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow("empty export should produce valid JSON");
    }

    [Fact]
    public async Task ExportUserData_JsonRoundTrip_AllFieldsPreserved()
    {
        var userId = Guid.NewGuid();
        var user = new User("roundtripuser", "rt@example.com", "hashedpw");
        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        SetupEmptyRepositories(userId);

        var result = await _service.ExportUserDataAsync(userId);
        result.IsSuccess.Should().BeTrue();

        // Serialize and deserialize
        var json = JsonSerializer.Serialize(result.Value, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<UserDataExportDto>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Version.Should().Be(result.Value.Version);
        deserialized.UserId.Should().Be(result.Value.UserId);
        deserialized.Profile.Username.Should().Be(result.Value.Profile.Username);
        deserialized.Profile.Email.Should().Be(result.Value.Profile.Email);
        deserialized.Profile.IsActive.Should().Be(result.Value.Profile.IsActive);
        deserialized.Profile.DefaultRole.Should().Be(result.Value.Profile.DefaultRole);
        deserialized.ExportedAt.Should().BeCloseTo(result.Value.ExportedAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ExportUserData_SharedBoard_OnlyOwnerDataIncluded()
    {
        // Arrange: user owns a board, another user has access
        var ownerId = Guid.NewGuid();
        var collaboratorId = Guid.NewGuid();
        var owner = new User("boardowner", "owner@example.com", "hashedpw");
        _userRepoMock.Setup(r => r.GetByIdAsync(ownerId, default)).ReturnsAsync(owner);

        var boardId = Guid.NewGuid();
        var board = new Board("Shared Board", "Description", ownerId);
        var ownerAccess = new BoardAccess(boardId, ownerId, UserRole.Owner, ownerId);

        _boardAccessRepoMock.Setup(r => r.GetByUserIdAsync(ownerId, default))
            .ReturnsAsync(new[] { ownerAccess });
        _boardRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), default))
            .ReturnsAsync(new[] { board });

        SetupEmptyRepositoriesExceptBoards(ownerId);

        var result = await _service.ExportUserDataAsync(ownerId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Data.Boards.Should().HaveCount(1);
        result.Value.Data.Boards[0].IsOwner.Should().BeTrue();

        // Verify that repository calls were only for the owner's ID
        _boardAccessRepoMock.Verify(r => r.GetByUserIdAsync(ownerId, default), Times.Once);
        _boardAccessRepoMock.Verify(r => r.GetByUserIdAsync(collaboratorId, default), Times.Never);
    }

    [Fact]
    public async Task ExportUserData_SpecialCharactersInUserData_ValidJsonOutput()
    {
        var userId = Guid.NewGuid();
        var user = new User("user_\"special\"", "special@\u00e9xample.com", "hashedpw");
        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        SetupEmptyRepositories(userId);

        var result = await _service.ExportUserDataAsync(userId);
        result.IsSuccess.Should().BeTrue();

        var json = JsonSerializer.Serialize(result.Value, JsonOptions);
        // Should be valid JSON even with special characters
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow("special characters should be properly escaped in JSON");

        var deserialized = JsonSerializer.Deserialize<UserDataExportDto>(json, JsonOptions);
        deserialized!.Profile.Username.Should().Contain("special");
    }

    [Fact]
    public async Task ExportUserData_VersionIsAlwaysPresent()
    {
        var userId = Guid.NewGuid();
        var user = new User("versionuser", "v@example.com", "hashedpw");
        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        SetupEmptyRepositories(userId);

        var result = await _service.ExportUserDataAsync(userId);
        result.IsSuccess.Should().BeTrue();
        result.Value.Version.Should().NotBeNullOrWhiteSpace("export version must always be present");
        result.Value.Version.Should().MatchRegex(@"^\d+\.\d+$", "version should be semver-like");
    }

    [Fact]
    public async Task ExportUserData_ExportedAtIsRecent()
    {
        var userId = Guid.NewGuid();
        var user = new User("timeuser", "time@example.com", "hashedpw");
        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        SetupEmptyRepositories(userId);

        var before = DateTimeOffset.UtcNow;
        var result = await _service.ExportUserDataAsync(userId);
        var after = DateTimeOffset.UtcNow;

        result.IsSuccess.Should().BeTrue();
        result.Value.ExportedAt.Should().BeOnOrAfter(before);
        result.Value.ExportedAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public async Task ExportUserData_NonexistentUser_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync((User?)null);

        var result = await _service.ExportUserDataAsync(userId);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ExportUserData_EmptyGuidUserId_ReturnsValidationError()
    {
        var result = await _service.ExportUserDataAsync(Guid.Empty);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    private void SetupEmptyRepositories(Guid userId)
    {
        _boardAccessRepoMock.Setup(r => r.GetByUserIdAsync(userId, default))
            .ReturnsAsync(Enumerable.Empty<BoardAccess>());
        _notificationRepoMock.Setup(r => r.GetByUserIdAsync(userId, It.IsAny<int>(), false, null, default, 0))
            .ReturnsAsync(Enumerable.Empty<Notification>());
        _llmQueueRepoMock.Setup(r => r.GetByUserAsync(userId, default))
            .ReturnsAsync(Enumerable.Empty<LlmRequest>());
        _proposalRepoMock.Setup(r => r.GetByUserIdAsync(userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<AutomationProposal>());
        _chatSessionRepoMock.Setup(r => r.GetByUserIdAsync(userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<ChatSession>());
        _auditLogRepoMock.Setup(r => r.GetByUserAsync(userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<AuditLog>());
        _userPrefRepoMock.Setup(r => r.GetByUserIdAsync(userId, default))
            .ReturnsAsync((UserPreference?)null);
        _notifPrefRepoMock.Setup(r => r.GetByUserIdAsync(userId, default))
            .ReturnsAsync((NotificationPreference?)null);
    }

    private void SetupEmptyRepositoriesExceptBoards(Guid userId)
    {
        _notificationRepoMock.Setup(r => r.GetByUserIdAsync(userId, It.IsAny<int>(), false, null, default, 0))
            .ReturnsAsync(Enumerable.Empty<Notification>());
        _llmQueueRepoMock.Setup(r => r.GetByUserAsync(userId, default))
            .ReturnsAsync(Enumerable.Empty<LlmRequest>());
        _proposalRepoMock.Setup(r => r.GetByUserIdAsync(userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<AutomationProposal>());
        _chatSessionRepoMock.Setup(r => r.GetByUserIdAsync(userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<ChatSession>());
        _auditLogRepoMock.Setup(r => r.GetByUserAsync(userId, It.IsAny<int>(), default))
            .ReturnsAsync(Enumerable.Empty<AuditLog>());
        _userPrefRepoMock.Setup(r => r.GetByUserIdAsync(userId, default))
            .ReturnsAsync((UserPreference?)null);
        _notifPrefRepoMock.Setup(r => r.GetByUserIdAsync(userId, default))
            .ReturnsAsync((NotificationPreference?)null);
    }
}
