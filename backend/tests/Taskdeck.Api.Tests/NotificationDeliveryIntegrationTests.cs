using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests for notification delivery, deduplication, and preference
/// filtering. Uses TestWebApplicationFactory for full API round-trips and
/// direct service-level verification against real SQLite.
/// Covers issue #719 scenarios: delivery, deduplication, preference filtering,
/// cross-user isolation, pagination, batch operations, and mark read/unread.
/// </summary>
public class NotificationDeliveryIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public NotificationDeliveryIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ────────────────────────────────────────────────────────────
    // Notification Delivery
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_MentionType_CreatesNotificationForMentionedUser()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = new User("mention-target-dlv", "mention-target-dlv@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await notificationService.PublishAsync(new CreateNotificationRequestDto(
            user.Id,
            NotificationType.Mention,
            "You were mentioned",
            "@someone mentioned you on card 'Fix login bug'.",
            SourceEntityType: "card-comment",
            SourceEntityId: Guid.NewGuid(),
            DeduplicationKey: $"mention:dlv:{Guid.NewGuid():N}"));
        await unitOfWork.SaveChangesAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue("notification should be created when preferences allow mentions");

        var notifications = await db.Notifications
            .Where(n => n.UserId == user.Id)
            .ToListAsync();
        notifications.Should().ContainSingle();
        notifications[0].Type.Should().Be(NotificationType.Mention);
        notifications[0].Cadence.Should().Be(NotificationCadence.Immediate);
    }

    [Fact]
    public async Task PublishAsync_AssignmentType_CreatesNotificationForAssignee()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = new User("assign-target-dlv", "assign-target-dlv@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await notificationService.PublishAsync(new CreateNotificationRequestDto(
            user.Id,
            NotificationType.Assignment,
            "Board access granted",
            "You were granted access to board 'Sprint 42'.",
            BoardId: Guid.NewGuid(),
            SourceEntityType: "board-access",
            SourceEntityId: Guid.NewGuid(),
            DeduplicationKey: $"assign:dlv:{Guid.NewGuid():N}"));
        await unitOfWork.SaveChangesAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();

        var notification = db.Notifications.Single(n => n.UserId == user.Id);
        notification.Type.Should().Be(NotificationType.Assignment);
    }

    [Fact]
    public async Task PublishAsync_ProposalOutcomeType_CreatesNotificationForProposalOwner()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = new User("proposal-owner-dlv", "proposal-owner-dlv@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await notificationService.PublishAsync(new CreateNotificationRequestDto(
            user.Id,
            NotificationType.ProposalOutcome,
            "Proposal approved",
            "Your proposal 'Move card to Done' was approved.",
            SourceEntityType: "automation-proposal",
            SourceEntityId: Guid.NewGuid(),
            DeduplicationKey: $"proposal:dlv:{Guid.NewGuid():N}"));
        await unitOfWork.SaveChangesAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();

        var notification = db.Notifications.Single(n => n.UserId == user.Id);
        notification.Type.Should().Be(NotificationType.ProposalOutcome);
    }

    [Fact]
    public async Task PublishAsync_BoardChangeType_CreatesNotification()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = new User("boardchange-dlv", "boardchange-dlv@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var boardId = Guid.NewGuid();
        var result = await notificationService.PublishAsync(new CreateNotificationRequestDto(
            user.Id,
            NotificationType.BoardChange,
            "Board updated",
            "Column 'Done' was added to your board.",
            BoardId: boardId,
            DeduplicationKey: $"boardchange:dlv:{Guid.NewGuid():N}"));
        await unitOfWork.SaveChangesAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();

        var notification = db.Notifications.Single(n => n.UserId == user.Id);
        notification.Type.Should().Be(NotificationType.BoardChange);
        notification.BoardId.Should().Be(boardId);
    }

    [Fact]
    public async Task PublishAsync_SystemType_CreatesNotification()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = new User("system-notif-dlv", "system-notif-dlv@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await notificationService.PublishAsync(new CreateNotificationRequestDto(
            user.Id,
            NotificationType.System,
            "Welcome",
            "Welcome to Taskdeck!",
            DeduplicationKey: $"system:dlv:{Guid.NewGuid():N}"));
        await unitOfWork.SaveChangesAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();

        var notification = db.Notifications.Single(n => n.UserId == user.Id);
        notification.Type.Should().Be(NotificationType.System);
    }

    // ────────────────────────────────────────────────────────────
    // Deduplication
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_DuplicateDeduplicationKey_SecondCallReturnsfalse()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = new User("dedup-same-event", "dedup-same-event@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var dedupKey = $"dedup:same-event:{Guid.NewGuid():N}";
        var request = new CreateNotificationRequestDto(
            user.Id,
            NotificationType.Mention,
            "Mention notification",
            "You were mentioned",
            DeduplicationKey: dedupKey);

        var first = await notificationService.PublishAsync(request);
        await unitOfWork.SaveChangesAsync();

        var second = await notificationService.PublishAsync(request);

        first.IsSuccess.Should().BeTrue();
        first.Value.Should().BeTrue("first publish should create the notification");
        second.IsSuccess.Should().BeTrue();
        second.Value.Should().BeFalse("second publish with same dedup key should be deduplicated");

        var count = db.Notifications.Count(n => n.UserId == user.Id && n.DeduplicationKey == dedupKey);
        count.Should().Be(1, "only one notification should exist for duplicated key");
    }

    [Fact]
    public async Task PublishAsync_SameDeduplicationKeyWithinUnitOfWork_SecondCallDeduplicated()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = new User("dedup-uow", "dedup-uow@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var dedupKey = $"dedup:uow:{Guid.NewGuid():N}";
        var request = new CreateNotificationRequestDto(
            user.Id,
            NotificationType.Mention,
            "Double mention",
            "Same person mentioned twice in one comment",
            DeduplicationKey: dedupKey);

        // Publish twice before saving - simulates mentioning same user twice
        var first = await notificationService.PublishAsync(request);
        var second = await notificationService.PublishAsync(request);
        await unitOfWork.SaveChangesAsync();

        first.IsSuccess.Should().BeTrue();
        first.Value.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value.Should().BeFalse("in-memory dedup cache should prevent second add");

        var count = db.Notifications.Count(n => n.UserId == user.Id && n.DeduplicationKey == dedupKey);
        count.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_DifferentDeduplicationKeys_BothCreated()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = new User("dedup-diff-keys", "dedup-diff-keys@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var key1 = $"dedup:key1:{Guid.NewGuid():N}";
        var key2 = $"dedup:key2:{Guid.NewGuid():N}";

        var result1 = await notificationService.PublishAsync(new CreateNotificationRequestDto(
            user.Id, NotificationType.Mention, "First", "First mention", DeduplicationKey: key1));
        var result2 = await notificationService.PublishAsync(new CreateNotificationRequestDto(
            user.Id, NotificationType.Mention, "Second", "Second mention", DeduplicationKey: key2));
        await unitOfWork.SaveChangesAsync();

        result1.Value.Should().BeTrue();
        result2.Value.Should().BeTrue();

        var count = db.Notifications.Count(n => n.UserId == user.Id);
        count.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task PublishAsync_WithoutDeduplicationKey_DuplicatesAllowed()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = new User("no-dedup-key", "no-dedup-key@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var request = new CreateNotificationRequestDto(
            user.Id,
            NotificationType.System,
            "System alert",
            "Something happened");

        var first = await notificationService.PublishAsync(request);
        var second = await notificationService.PublishAsync(request);
        await unitOfWork.SaveChangesAsync();

        first.Value.Should().BeTrue();
        second.Value.Should().BeTrue("no dedup key means duplicates are allowed");

        var count = db.Notifications.Count(n => n.UserId == user.Id);
        count.Should().BeGreaterOrEqualTo(2);
    }

    // ────────────────────────────────────────────────────────────
    // Preference Filtering
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_WhenMentionImmediateDisabled_AndDigestDisabled_SkipsNotification()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = new User("pref-mention-off", "pref-mention-off@example.com", "hash");
        db.Users.Add(user);
        var preference = new NotificationPreference(
            user.Id,
            inAppChannelEnabled: true,
            mentionImmediateEnabled: false,
            mentionDigestEnabled: false,
            assignmentImmediateEnabled: true,
            assignmentDigestEnabled: false,
            proposalOutcomeImmediateEnabled: true,
            proposalOutcomeDigestEnabled: false);
        db.NotificationPreferences.Add(preference);
        await db.SaveChangesAsync();

        var result = await notificationService.PublishAsync(new CreateNotificationRequestDto(
            user.Id,
            NotificationType.Mention,
            "Mention",
            "You were mentioned",
            DeduplicationKey: $"pref:mention-off:{Guid.NewGuid():N}"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse("mention notifications should be filtered when both immediate and digest are disabled");

        db.Notifications.Count(n => n.UserId == user.Id).Should().Be(0);
    }

    [Fact]
    public async Task PublishAsync_WhenInAppChannelDisabled_SkipsAllNotifications()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = new User("pref-all-off", "pref-all-off@example.com", "hash");
        db.Users.Add(user);
        var preference = new NotificationPreference(
            user.Id,
            inAppChannelEnabled: false,
            mentionImmediateEnabled: true,
            mentionDigestEnabled: true,
            assignmentImmediateEnabled: true,
            assignmentDigestEnabled: true,
            proposalOutcomeImmediateEnabled: true,
            proposalOutcomeDigestEnabled: true);
        db.NotificationPreferences.Add(preference);
        await db.SaveChangesAsync();

        var result = await notificationService.PublishAsync(new CreateNotificationRequestDto(
            user.Id,
            NotificationType.Mention,
            "Mention",
            "You were mentioned",
            DeduplicationKey: $"pref:all-off:{Guid.NewGuid():N}"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse("no notifications when in-app channel is disabled");

        db.Notifications.Count(n => n.UserId == user.Id).Should().Be(0);
    }

    [Fact]
    public async Task PublishAsync_WhenAssignmentDisabled_SkipsAssignmentButAllowsMention()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = new User("pref-assign-off", "pref-assign-off@example.com", "hash");
        db.Users.Add(user);
        var preference = new NotificationPreference(
            user.Id,
            inAppChannelEnabled: true,
            mentionImmediateEnabled: true,
            mentionDigestEnabled: false,
            assignmentImmediateEnabled: false,
            assignmentDigestEnabled: false,
            proposalOutcomeImmediateEnabled: true,
            proposalOutcomeDigestEnabled: false);
        db.NotificationPreferences.Add(preference);
        await db.SaveChangesAsync();

        var assignResult = await notificationService.PublishAsync(new CreateNotificationRequestDto(
            user.Id,
            NotificationType.Assignment,
            "Assigned",
            "You were assigned",
            DeduplicationKey: $"pref:assign-off-a:{Guid.NewGuid():N}"));

        var mentionResult = await notificationService.PublishAsync(new CreateNotificationRequestDto(
            user.Id,
            NotificationType.Mention,
            "Mentioned",
            "You were mentioned",
            DeduplicationKey: $"pref:assign-off-m:{Guid.NewGuid():N}"));
        await unitOfWork.SaveChangesAsync();

        assignResult.Value.Should().BeFalse("assignment notifications disabled");
        mentionResult.Value.Should().BeTrue("mention notifications still enabled");

        var notifications = db.Notifications.Where(n => n.UserId == user.Id).ToList();
        notifications.Should().ContainSingle();
        notifications[0].Type.Should().Be(NotificationType.Mention);
    }

    [Fact]
    public async Task PublishAsync_WhenDigestOnlyEnabled_CreatesWithDigestCadence()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = new User("pref-digest-only", "pref-digest-only@example.com", "hash");
        db.Users.Add(user);
        var preference = new NotificationPreference(
            user.Id,
            inAppChannelEnabled: true,
            mentionImmediateEnabled: false,
            mentionDigestEnabled: true,
            assignmentImmediateEnabled: true,
            assignmentDigestEnabled: false,
            proposalOutcomeImmediateEnabled: true,
            proposalOutcomeDigestEnabled: false);
        db.NotificationPreferences.Add(preference);
        await db.SaveChangesAsync();

        var result = await notificationService.PublishAsync(new CreateNotificationRequestDto(
            user.Id,
            NotificationType.Mention,
            "Mention digest",
            "You were mentioned (digest)",
            DeduplicationKey: $"pref:digest:{Guid.NewGuid():N}"));
        await unitOfWork.SaveChangesAsync();

        result.Value.Should().BeTrue();

        var notification = db.Notifications.Single(n => n.UserId == user.Id);
        notification.Cadence.Should().Be(NotificationCadence.Digest,
            "when immediate is off but digest is on, cadence should be Digest");
    }

    [Fact]
    public async Task PreferenceChangeIsImmediate_NextEventRespectsNewPreference()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = new User("pref-change-imm", "pref-change-imm@example.com", "hash");
        db.Users.Add(user);
        // Start with defaults (mentions enabled)
        await db.SaveChangesAsync();

        // Publish first — should succeed
        var first = await notificationService.PublishAsync(new CreateNotificationRequestDto(
            user.Id, NotificationType.Mention, "Before change", "Msg1",
            DeduplicationKey: $"pref:change1:{Guid.NewGuid():N}"));
        first.Value.Should().BeTrue();

        // Now disable mentions via preferences update
        await notificationService.UpdatePreferencesAsync(user.Id, new UpdateNotificationPreferenceDto(
            InAppChannelEnabled: true,
            MentionImmediateEnabled: false,
            MentionDigestEnabled: false,
            AssignmentImmediateEnabled: true,
            AssignmentDigestEnabled: false,
            ProposalOutcomeImmediateEnabled: true,
            ProposalOutcomeDigestEnabled: false));

        // Use a new service instance so preference cache is cleared
        using var scope2 = _factory.Services.CreateScope();
        var notificationService2 = scope2.ServiceProvider.GetRequiredService<INotificationService>();

        var second = await notificationService2.PublishAsync(new CreateNotificationRequestDto(
            user.Id, NotificationType.Mention, "After change", "Msg2",
            DeduplicationKey: $"pref:change2:{Guid.NewGuid():N}"));
        second.Value.Should().BeFalse("preference change should be respected immediately");
    }

    [Fact]
    public async Task PublishAsync_BoardChangeAlwaysEnabled_EvenWithMinimalPreferences()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = new User("pref-boardchange", "pref-boardchange@example.com", "hash");
        db.Users.Add(user);
        // Disable all configurable notifications, but in-app remains on
        var preference = new NotificationPreference(
            user.Id,
            inAppChannelEnabled: true,
            mentionImmediateEnabled: false,
            mentionDigestEnabled: false,
            assignmentImmediateEnabled: false,
            assignmentDigestEnabled: false,
            proposalOutcomeImmediateEnabled: false,
            proposalOutcomeDigestEnabled: false);
        db.NotificationPreferences.Add(preference);
        await db.SaveChangesAsync();

        var result = await notificationService.PublishAsync(new CreateNotificationRequestDto(
            user.Id,
            NotificationType.BoardChange,
            "Board change",
            "Column added",
            DeduplicationKey: $"pref:boardchange:{Guid.NewGuid():N}"));
        await unitOfWork.SaveChangesAsync();

        result.Value.Should().BeTrue("BoardChange type always has immediate enabled regardless of preferences");
    }

    // ────────────────────────────────────────────────────────────
    // Cross-User Isolation
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetNotifications_UserA_NeverSeesUserB_Notifications()
    {
        using var clientA = _factory.CreateClient();
        using var clientB = _factory.CreateClient();
        var userA = await ApiTestHarness.AuthenticateAsync(clientA, "iso-userA-ntf");
        var userB = await ApiTestHarness.AuthenticateAsync(clientB, "iso-userB-ntf");

        // Seed a notification for user B
        await SeedNotificationAsync(userB.UserId, title: "UserB only notification");

        // User A should not see it
        var responseA = await clientA.GetAsync("/api/notifications");
        responseA.StatusCode.Should().Be(HttpStatusCode.OK);
        var notificationsA = await responseA.Content.ReadFromJsonAsync<NotificationDto[]>();
        notificationsA.Should().NotBeNull();
        notificationsA!.Should().NotContain(n => n.Title == "UserB only notification");
    }

    [Fact]
    public async Task MarkAllRead_ForUserA_DoesNotAffectUserB_UnreadCount()
    {
        using var clientA = _factory.CreateClient();
        using var clientB = _factory.CreateClient();
        var userA = await ApiTestHarness.AuthenticateAsync(clientA, "iso-markall-a");
        var userB = await ApiTestHarness.AuthenticateAsync(clientB, "iso-markall-b");

        await SeedNotificationAsync(userA.UserId, title: "UserA notification");
        await SeedNotificationAsync(userB.UserId, title: "UserB notification");

        // Mark all read for user A
        var markResponse = await clientA.PostAsync("/api/notifications/mark-all-read", null);
        markResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // User B's notifications should still be unread
        var responseB = await clientB.GetAsync("/api/notifications?unreadOnly=true");
        responseB.StatusCode.Should().Be(HttpStatusCode.OK);
        var notificationsB = await responseB.Content.ReadFromJsonAsync<NotificationDto[]>();
        notificationsB.Should().NotBeNull();
        notificationsB!.Should().Contain(n => n.Title == "UserB notification" && !n.IsRead);
    }

    // ────────────────────────────────────────────────────────────
    // Mark as Read / Unread
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkAsRead_SetsIsReadAndReadAt()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "mark-read-basic");
        var notification = await SeedNotificationAsync(user.UserId);

        var response = await client.PostAsync($"/api/notifications/{notification.Id}/read", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<NotificationDto>();
        result.Should().NotBeNull();
        result!.IsRead.Should().BeTrue();
        result.ReadAt.Should().NotBeNull();
        result.ReadAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MarkAsRead_AlreadyRead_IdempotentSuccess()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "mark-read-idem");
        var notification = await SeedNotificationAsync(user.UserId);

        // Mark once
        await client.PostAsync($"/api/notifications/{notification.Id}/read", null);
        // Mark again
        var response = await client.PostAsync($"/api/notifications/{notification.Id}/read", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<NotificationDto>();
        result!.IsRead.Should().BeTrue();
    }

    [Fact]
    public async Task MarkAsRead_NonExistentId_Returns404()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "mark-read-404");

        var response = await client.PostAsync($"/api/notifications/{Guid.NewGuid()}/read", null);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MarkAsRead_CrossUser_ReturnsForbidden()
    {
        using var ownerClient = _factory.CreateClient();
        using var otherClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "mark-read-cross-own");
        await ApiTestHarness.AuthenticateAsync(otherClient, "mark-read-cross-oth");

        var notification = await SeedNotificationAsync(owner.UserId);

        var response = await otherClient.PostAsync($"/api/notifications/{notification.Id}/read", null);

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    // ────────────────────────────────────────────────────────────
    // Batch Mark All Read
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkAllRead_MarksAllUnreadAndReturnsCount()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "markall-count");

        await SeedNotificationAsync(user.UserId, title: "Unread 1");
        await SeedNotificationAsync(user.UserId, title: "Unread 2");
        await SeedNotificationAsync(user.UserId, title: "Unread 3");

        var response = await client.PostAsync("/api/notifications/mark-all-read", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"markedCount\":");

        // Verify all are now read
        var getResponse = await client.GetAsync("/api/notifications?unreadOnly=true");
        var unread = await getResponse.Content.ReadFromJsonAsync<NotificationDto[]>();
        unread.Should().NotBeNull();
        unread!.Should().BeEmpty("all notifications should be marked as read");
    }

    [Fact]
    public async Task MarkAllRead_WithBoardFilter_OnlyMarksBoardNotifications()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "markall-board");
        var board = await ApiTestHarness.CreateBoardAsync(client, "markall-board");

        var boardNotification = await SeedNotificationAsync(user.UserId, boardId: board.Id, title: "Board notif");
        var globalNotification = await SeedNotificationAsync(user.UserId, title: "Global notif");

        var response = await client.PostAsync($"/api/notifications/mark-all-read?boardId={board.Id}", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Global notification should still be unread
        var getResponse = await client.GetAsync("/api/notifications?unreadOnly=true");
        var unread = await getResponse.Content.ReadFromJsonAsync<NotificationDto[]>();
        unread.Should().NotBeNull();
        unread!.Should().Contain(n => n.Id == globalNotification.Id, "global notification should remain unread");
        unread.Should().NotContain(n => n.Id == boardNotification.Id, "board notification should be marked read");
    }

    [Fact]
    public async Task MarkAllRead_NoUnread_ReturnsZeroCount()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "markall-zero");

        var response = await client.PostAsync("/api/notifications/mark-all-read", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"markedCount\":0");
    }

    // ────────────────────────────────────────────────────────────
    // Pagination
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetNotifications_Pagination_LimitReturnsCorrectCount()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "pag-limit");

        for (var i = 0; i < 10; i++)
        {
            await SeedNotificationAsync(user.UserId, title: $"Pag notif {i}");
        }

        var response = await client.GetAsync("/api/notifications?limit=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var notifications = await response.Content.ReadFromJsonAsync<NotificationDto[]>();
        notifications.Should().NotBeNull();
        notifications!.Length.Should().BeLessOrEqualTo(5);
    }

    [Fact]
    public async Task GetNotifications_UnreadOnlyFilter_ExcludesRead()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "pag-unread");

        var readNotif = await SeedNotificationAsync(user.UserId, title: "Read notif");
        await SeedNotificationAsync(user.UserId, title: "Unread notif");

        // Mark one as read
        await client.PostAsync($"/api/notifications/{readNotif.Id}/read", null);

        var response = await client.GetAsync("/api/notifications?unreadOnly=true");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var notifications = await response.Content.ReadFromJsonAsync<NotificationDto[]>();
        notifications.Should().NotBeNull();
        notifications!.Should().NotContain(n => n.Id == readNotif.Id);
        notifications.Should().Contain(n => n.Title == "Unread notif");
    }

    [Fact]
    public async Task GetNotifications_BoardFilter_OnlyReturnsBoardNotifications()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "pag-boardfilt");
        var board = await ApiTestHarness.CreateBoardAsync(client, "pag-boardfilt");

        await SeedNotificationAsync(user.UserId, boardId: board.Id, title: "Board-scoped notif");
        await SeedNotificationAsync(user.UserId, title: "No-board notif");

        var response = await client.GetAsync($"/api/notifications?boardId={board.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var notifications = await response.Content.ReadFromJsonAsync<NotificationDto[]>();
        notifications.Should().NotBeNull();
        notifications!.Should().OnlyContain(n => n.BoardId == board.Id);
    }

    [Fact]
    public async Task GetNotifications_InvalidLimit_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "pag-invalid-limit");

        var response = await client.GetAsync("/api/notifications?limit=0");

        // The service returns a validation error which maps to 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetNotifications_ExceedMaxLimit_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "pag-max-limit");

        var response = await client.GetAsync("/api/notifications?limit=501");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ────────────────────────────────────────────────────────────
    // Preferences API (HTTP round-trip)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPreferences_DefaultsCreatedOnFirstAccess()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "pref-default");

        var response = await client.GetAsync("/api/notifications/preferences");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var prefs = await response.Content.ReadFromJsonAsync<NotificationPreferenceDto>();
        prefs.Should().NotBeNull();
        prefs!.InAppChannelEnabled.Should().BeTrue();
        prefs.MentionImmediateEnabled.Should().BeTrue();
        prefs.AssignmentImmediateEnabled.Should().BeTrue();
        prefs.ProposalOutcomeImmediateEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task UpdatePreferences_PersistsAndReturnsUpdatedValues()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "pref-update");

        var updateDto = new UpdateNotificationPreferenceDto(
            InAppChannelEnabled: false,
            MentionImmediateEnabled: false,
            MentionDigestEnabled: true,
            AssignmentImmediateEnabled: false,
            AssignmentDigestEnabled: true,
            ProposalOutcomeImmediateEnabled: false,
            ProposalOutcomeDigestEnabled: true);

        var response = await client.PutAsJsonAsync("/api/notifications/preferences", updateDto);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<NotificationPreferenceDto>();
        updated.Should().NotBeNull();
        updated!.InAppChannelEnabled.Should().BeFalse();
        updated.MentionImmediateEnabled.Should().BeFalse();
        updated.MentionDigestEnabled.Should().BeTrue();

        // Verify persistence via GET
        var getResponse = await client.GetAsync("/api/notifications/preferences");
        var persisted = await getResponse.Content.ReadFromJsonAsync<NotificationPreferenceDto>();
        persisted!.InAppChannelEnabled.Should().BeFalse();
        persisted.MentionDigestEnabled.Should().BeTrue();
    }

    // ────────────────────────────────────────────────────────────
    // Auth enforcement
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AllEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var getNotifs = await client.GetAsync("/api/notifications");
        await ApiTestHarness.AssertUnauthorizedAsync(getNotifs);

        var markRead = await client.PostAsync($"/api/notifications/{Guid.NewGuid()}/read", null);
        await ApiTestHarness.AssertUnauthorizedAsync(markRead);

        var markAllRead = await client.PostAsync("/api/notifications/mark-all-read", null);
        await ApiTestHarness.AssertUnauthorizedAsync(markAllRead);

        var getPrefs = await client.GetAsync("/api/notifications/preferences");
        await ApiTestHarness.AssertUnauthorizedAsync(getPrefs);

        var updatePrefs = await client.PutAsJsonAsync("/api/notifications/preferences",
            new UpdateNotificationPreferenceDto(true, true, false, true, false, true, false));
        await ApiTestHarness.AssertUnauthorizedAsync(updatePrefs);
    }

    // ────────────────────────────────────────────────────────────
    // Notification creation with defaults (no pre-existing preferences)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_NewUser_AutoCreatesDefaultPreferencesAndDelivers()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var user = new User("auto-pref-user", "auto-pref-user@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // No preferences seeded — PublishAsync should auto-create defaults
        var result = await notificationService.PublishAsync(new CreateNotificationRequestDto(
            user.Id,
            NotificationType.Mention,
            "Welcome mention",
            "You were mentioned",
            DeduplicationKey: $"auto-pref:{Guid.NewGuid():N}"));
        await unitOfWork.SaveChangesAsync();

        result.Value.Should().BeTrue("default preferences enable mention immediate");

        var prefs = await db.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == user.Id);
        prefs.Should().NotBeNull("preferences should be auto-created");
        prefs!.MentionImmediateEnabled.Should().BeTrue();
    }

    // ────────────────────────────────────────────────────────────
    // Performance-relevant: many notifications
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetNotifications_WithManyNotifications_DoesNotTimeout()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var user = new User("perf-many-notifs", "perf-many-notifs@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Seed 200 notifications (enough to test pagination behavior, not so many as to slow tests)
        var notifications = new List<Notification>();
        for (var i = 0; i < 200; i++)
        {
            notifications.Add(new Notification(
                user.Id,
                NotificationType.System,
                NotificationCadence.Immediate,
                $"Perf notification {i}",
                $"Performance test message {i}"));
        }
        db.Notifications.AddRange(notifications);
        await db.SaveChangesAsync();

        var start = DateTimeOffset.UtcNow;
        var results = (await repo.GetByUserIdAsync(user.Id, limit: 20, offset: 0)).ToList();
        var elapsed = DateTimeOffset.UtcNow - start;

        results.Count.Should().Be(20);
        elapsed.TotalSeconds.Should().BeLessThan(5, "pagination query should complete within 5 seconds");

        // Verify offset beyond total returns empty
        var emptyPage = (await repo.GetByUserIdAsync(user.Id, limit: 20, offset: 200)).ToList();
        emptyPage.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────
    // Board-scoped notification isolation via API
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetNotifications_ForeignBoardFilter_ReturnsForbidden()
    {
        using var ownerClient = _factory.CreateClient();
        using var otherClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(ownerClient, "board-iso-own");
        await ApiTestHarness.AuthenticateAsync(otherClient, "board-iso-oth");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "board-iso");

        var response = await otherClient.GetAsync($"/api/notifications?boardId={board.Id}");

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task MarkAllRead_ForeignBoardFilter_ReturnsForbidden()
    {
        using var ownerClient = _factory.CreateClient();
        using var otherClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(ownerClient, "markall-iso-own");
        await ApiTestHarness.AuthenticateAsync(otherClient, "markall-iso-oth");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "markall-iso");

        var response = await otherClient.PostAsync($"/api/notifications/mark-all-read?boardId={board.Id}", null);

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    // ────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────

    private async Task<Notification> SeedNotificationAsync(
        Guid userId,
        Guid? boardId = null,
        string title = "Test notification",
        NotificationType type = NotificationType.Mention)
    {
        var notification = new Notification(
            userId,
            type,
            NotificationCadence.Immediate,
            title,
            "Seeded notification for integration testing.",
            boardId,
            sourceEntityType: "test",
            sourceEntityId: Guid.NewGuid(),
            deduplicationKey: $"seed:{Guid.NewGuid():N}");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();
        return notification;
    }
}
