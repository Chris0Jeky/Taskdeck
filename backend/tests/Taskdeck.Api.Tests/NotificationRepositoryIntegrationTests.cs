using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests for NotificationRepository against real SQLite.
/// Covers pagination correctness, unread filtering, board filtering,
/// deduplication key lookup, and cross-user isolation.
/// </summary>
public class NotificationRepositoryIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public NotificationRepositoryIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetByUserIdAsync_ShouldReturnOnlyUserNotifications()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var userA = new User("notif-usera", "notif-usera@example.com", "hash");
        var userB = new User("notif-userb", "notif-userb@example.com", "hash");
        db.Users.AddRange(userA, userB);

        var notifA = new Notification(userA.Id, NotificationType.System, NotificationCadence.Immediate,
            "Notif for A", "Message for A");
        var notifB = new Notification(userB.Id, NotificationType.System, NotificationCadence.Immediate,
            "Notif for B", "Message for B");
        db.Notifications.AddRange(notifA, notifB);
        await db.SaveChangesAsync();

        var resultsA = (await repo.GetByUserIdAsync(userA.Id)).ToList();

        resultsA.Should().Contain(n => n.Id == notifA.Id);
        resultsA.Should().NotContain(n => n.Id == notifB.Id);
    }

    [Fact]
    public async Task GetByUserIdAsync_WithUnreadOnly_ShouldExcludeReadNotifications()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var user = new User("notif-unread-user", "notif-unread@example.com", "hash");
        db.Users.Add(user);

        var unread = new Notification(user.Id, NotificationType.System, NotificationCadence.Immediate,
            "Unread notif", "Still unread");
        var read = new Notification(user.Id, NotificationType.System, NotificationCadence.Immediate,
            "Read notif", "Already read");
        read.MarkAsRead();
        db.Notifications.AddRange(unread, read);
        await db.SaveChangesAsync();

        var results = (await repo.GetByUserIdAsync(user.Id, unreadOnly: true)).ToList();

        results.Should().Contain(n => n.Id == unread.Id);
        results.Should().NotContain(n => n.Id == read.Id);
    }

    [Fact]
    public async Task GetByUserIdAsync_WithBoardFilter_ShouldFilterByBoard()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var user = new User("notif-brd-user", "notif-brd@example.com", "hash");
        db.Users.Add(user);

        var boardA = new Board("Notif board A", ownerId: user.Id);
        var boardB = new Board("Notif board B", ownerId: user.Id);
        db.Boards.AddRange(boardA, boardB);

        var notifA = new Notification(user.Id, NotificationType.BoardChange, NotificationCadence.Immediate,
            "Board A notif", "Change on A", boardId: boardA.Id);
        var notifB = new Notification(user.Id, NotificationType.BoardChange, NotificationCadence.Immediate,
            "Board B notif", "Change on B", boardId: boardB.Id);
        db.Notifications.AddRange(notifA, notifB);
        await db.SaveChangesAsync();

        var results = (await repo.GetByUserIdAsync(user.Id, boardId: boardA.Id)).ToList();

        results.Should().Contain(n => n.Id == notifA.Id);
        results.Should().NotContain(n => n.Id == notifB.Id);
    }

    [Fact]
    public async Task GetByUserIdAsync_WithPagination_ShouldRespectLimitAndOffset()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var user = new User("notif-page-user", "notif-page@example.com", "hash");
        db.Users.Add(user);

        var baseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var notifications = new List<Notification>();
        for (var i = 0; i < 5; i++)
        {
            var notif = new Notification(
                user.Id, NotificationType.System, NotificationCadence.Immediate,
                $"Page notif {i}", $"Message {i}");
            notifications.Add(notif);
            db.Notifications.Add(notif);
        }
        await db.SaveChangesAsync();

        // Set explicit timestamps for deterministic ordering
        for (var i = 0; i < notifications.Count; i++)
        {
            db.Entry(notifications[i]).Property(nameof(Entity.CreatedAt)).CurrentValue = baseTime.AddSeconds(i);
        }
        await db.SaveChangesAsync();

        var page1 = (await repo.GetByUserIdAsync(user.Id, limit: 2, offset: 0)).ToList();
        var page2 = (await repo.GetByUserIdAsync(user.Id, limit: 2, offset: 2)).ToList();

        page1.Count.Should().Be(2);
        page2.Count.Should().Be(2);

        // No overlap
        page1.Select(n => n.Id).Should().NotIntersectWith(page2.Select(n => n.Id));
    }

    [Fact]
    public async Task GetByUserIdAsync_WithOffsetBeyondTotal_ShouldReturnEmpty()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var user = new User("notif-beyond-user", "notif-beyond@example.com", "hash");
        db.Users.Add(user);

        db.Notifications.Add(new Notification(
            user.Id, NotificationType.System, NotificationCadence.Immediate,
            "Only one", "Only one message"));
        await db.SaveChangesAsync();

        var results = (await repo.GetByUserIdAsync(user.Id, limit: 10, offset: 100)).ToList();
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByUserAndDeduplicationKeyAsync_ShouldFindExactMatch()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var user = new User("notif-dedup-user", "notif-dedup@example.com", "hash");
        db.Users.Add(user);

        var dedupKey = $"dedup-{Guid.NewGuid():N}";
        var notif = new Notification(
            user.Id, NotificationType.ProposalOutcome, NotificationCadence.Immediate,
            "Dedup notif", "Dedup message", deduplicationKey: dedupKey);
        db.Notifications.Add(notif);
        await db.SaveChangesAsync();

        var found = await repo.GetByUserAndDeduplicationKeyAsync(user.Id, dedupKey);
        found.Should().NotBeNull();
        found!.Id.Should().Be(notif.Id);

        var notFound = await repo.GetByUserAndDeduplicationKeyAsync(user.Id, "nonexistent-key");
        notFound.Should().BeNull();
    }

    [Fact]
    public async Task GetByUserAndDeduplicationKeyAsync_ShouldIsolateByUser()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var userA = new User("notif-iso-a", "notif-iso-a@example.com", "hash");
        var userB = new User("notif-iso-b", "notif-iso-b@example.com", "hash");
        db.Users.AddRange(userA, userB);

        var sharedKey = $"shared-key-{Guid.NewGuid():N}";
        var notifA = new Notification(
            userA.Id, NotificationType.System, NotificationCadence.Immediate,
            "User A dedup", "Message A", deduplicationKey: sharedKey);
        db.Notifications.Add(notifA);
        await db.SaveChangesAsync();

        // User B with same key should not find User A's notification
        var result = await repo.GetByUserAndDeduplicationKeyAsync(userB.Id, sharedKey);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetUnreadByUserIdAsync_ShouldReturnOnlyUnread()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var user = new User("notif-unr-user", "notif-unr@example.com", "hash");
        db.Users.Add(user);

        var unread = new Notification(user.Id, NotificationType.System, NotificationCadence.Immediate,
            "Still unread", "Msg");
        var read = new Notification(user.Id, NotificationType.System, NotificationCadence.Immediate,
            "Already read", "Msg");
        read.MarkAsRead();
        db.Notifications.AddRange(unread, read);
        await db.SaveChangesAsync();

        var results = (await repo.GetUnreadByUserIdAsync(user.Id)).ToList();

        results.Should().Contain(n => n.Id == unread.Id);
        results.Should().NotContain(n => n.Id == read.Id);
    }

    [Fact]
    public async Task GetByUserIdAsync_ShouldOrderByCreatedAtDesc()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var user = new User("notif-ord-user", "notif-ord@example.com", "hash");
        db.Users.Add(user);

        var baseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var first = new Notification(user.Id, NotificationType.System, NotificationCadence.Immediate,
            "First notif", "Created first");
        var second = new Notification(user.Id, NotificationType.System, NotificationCadence.Immediate,
            "Second notif", "Created second");
        db.Notifications.AddRange(first, second);
        await db.SaveChangesAsync();

        // Set explicit timestamps for deterministic ordering
        db.Entry(first).Property(nameof(Entity.CreatedAt)).CurrentValue = baseTime;
        db.Entry(second).Property(nameof(Entity.CreatedAt)).CurrentValue = baseTime.AddSeconds(1);
        await db.SaveChangesAsync();

        var results = (await repo.GetByUserIdAsync(user.Id)).ToList();

        var firstIdx = results.FindIndex(n => n.Id == first.Id);
        var secondIdx = results.FindIndex(n => n.Id == second.Id);

        // Explicit assertions that items are present (not silently guarded)
        firstIdx.Should().BeGreaterOrEqualTo(0, "first item should be in results");
        secondIdx.Should().BeGreaterOrEqualTo(0, "second item should be in results");
        // DESC: second (newer) should appear before first (older)
        secondIdx.Should().BeLessThan(firstIdx, "DESC: newer before older");
    }
}
