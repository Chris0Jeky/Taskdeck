using System.Collections.Concurrent;
using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
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
        firstIdx.Should().BeGreaterThanOrEqualTo(0, "first item should be in results");
        secondIdx.Should().BeGreaterThanOrEqualTo(0, "second item should be in results");
        // DESC: second (newer) should appear before first (older)
        secondIdx.Should().BeLessThan(firstIdx, "DESC: newer before older");
    }

    /// <summary>
    /// Regression for #1133: paging must be pushed into SQL (ORDER BY + LIMIT/OFFSET),
    /// not done in memory after materializing every matching row. Seeds more rows than the
    /// page size and asserts the returned page is exactly the expected newest-first slice and
    /// is bounded to <c>limit</c> rows.
    /// </summary>
    [Fact]
    public async Task GetByUserIdAsync_WithPaging_ReturnsExactNewestFirstSliceBoundedToLimit()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var user = new User("notif-slice-user", "notif-slice@example.com", "hash");
        db.Users.Add(user);

        // Seed 5 notifications (> page size) with strictly increasing timestamps.
        var baseTime = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var notifications = new List<Notification>();
        for (var i = 0; i < 5; i++)
        {
            var notif = new Notification(
                user.Id, NotificationType.System, NotificationCadence.Immediate,
                $"Slice notif {i}", $"Message {i}");
            notifications.Add(notif);
            db.Notifications.Add(notif);
        }
        await db.SaveChangesAsync();

        for (var i = 0; i < notifications.Count; i++)
        {
            db.Entry(notifications[i]).Property(nameof(Entity.CreatedAt)).CurrentValue = baseTime.AddSeconds(i);
        }
        await db.SaveChangesAsync();

        // Newest-first order is index 4,3,2,1,0. With limit=2, offset=1 we expect [3, 2].
        var page = (await repo.GetByUserIdAsync(user.Id, limit: 2, offset: 1)).ToList();

        // Bounded: never more than the requested limit.
        page.Should().HaveCount(2);
        // Exact ordered slice (newest first, after skipping the single newest row).
        page[0].Id.Should().Be(notifications[3].Id);
        page[1].Id.Should().Be(notifications[2].Id);
    }

    /// <summary>
    /// Regression for #1133: when notifications share the same <c>CreatedAt</c>, offset paging
    /// must remain deterministic (no skipped or duplicated rows across pages). A secondary
    /// sort on <c>Id</c> provides the stable tiebreaker.
    /// </summary>
    [Fact]
    public async Task GetByUserIdAsync_WithTiedCreatedAt_PagesDeterministicallyWithoutGapsOrDuplicates()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var user = new User("notif-tie-user", "notif-tie@example.com", "hash");
        db.Users.Add(user);

        var notifications = new List<Notification>();
        for (var i = 0; i < 4; i++)
        {
            var notif = new Notification(
                user.Id, NotificationType.System, NotificationCadence.Immediate,
                $"Tie notif {i}", $"Message {i}");
            notifications.Add(notif);
            db.Notifications.Add(notif);
        }
        await db.SaveChangesAsync();

        // Two pairs of rows that share an identical timestamp (forces the tiebreaker).
        var groupA = new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var groupB = groupA.AddSeconds(1);
        db.Entry(notifications[0]).Property(nameof(Entity.CreatedAt)).CurrentValue = groupA;
        db.Entry(notifications[1]).Property(nameof(Entity.CreatedAt)).CurrentValue = groupA;
        db.Entry(notifications[2]).Property(nameof(Entity.CreatedAt)).CurrentValue = groupB;
        db.Entry(notifications[3]).Property(nameof(Entity.CreatedAt)).CurrentValue = groupB;
        await db.SaveChangesAsync();

        var page1 = (await repo.GetByUserIdAsync(user.Id, limit: 2, offset: 0)).ToList();
        var page2 = (await repo.GetByUserIdAsync(user.Id, limit: 2, offset: 2)).ToList();

        // Repeated identical query returns the same order (deterministic).
        var page1Again = (await repo.GetByUserIdAsync(user.Id, limit: 2, offset: 0)).ToList();
        page1Again.Select(n => n.Id).Should().Equal(page1.Select(n => n.Id));

        // The two pages together cover every row exactly once: no gaps, no duplicates.
        var combined = page1.Concat(page2).Select(n => n.Id).ToList();
        combined.Should().OnlyHaveUniqueItems();
        combined.Should().BeEquivalentTo(notifications.Select(n => n.Id));
    }

    /// <summary>
    /// Regression for #1133 (PR #1171 review, MEDIUM): the slice-correctness test alone cannot
    /// distinguish the SQL-paged implementation from the old materialize-then-page anti-pattern --
    /// both return the same rows. This test captures the SQL the repository actually executes and
    /// asserts the SELECT against Notifications carries LIMIT and OFFSET, so a revert to in-memory
    /// paging (which would emit no LIMIT/OFFSET on the SQLite path) fails the test.
    /// </summary>
    [Fact]
    public async Task GetByUserIdAsync_WithPaging_PushesLimitAndOffsetIntoSql()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-notif-sqlpaging-{Guid.NewGuid():N}.db");
        var interceptor = new CapturingCommandInterceptor();
        try
        {
            var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
                .UseSqlite(TestSqlite.ConnectionString(dbPath))
                .AddInterceptors(interceptor)
                .Options;

            await using (var db = new TaskdeckDbContext(options))
            {
                await db.Database.MigrateAsync();

                var user = new User("notif-sqlpaging-user", "notif-sqlpaging@example.com", "hash");
                db.Users.Add(user);
                for (var i = 0; i < 5; i++)
                {
                    db.Notifications.Add(new Notification(
                        user.Id, NotificationType.System, NotificationCadence.Immediate,
                        $"Sql paging notif {i}", $"Message {i}"));
                }
                await db.SaveChangesAsync();

                var repo = new NotificationRepository(db);

                // Discard migration/seed SQL so only the paged read remains captured.
                interceptor.Clear();
                var page = (await repo.GetByUserIdAsync(user.Id, limit: 2, offset: 1)).ToList();

                page.Should().HaveCount(2);

                var notificationSelects = interceptor.CapturedCommands
                    .Where(sql => sql.Contains("Notifications", StringComparison.OrdinalIgnoreCase)
                                  && sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                notificationSelects.Should().NotBeEmpty(
                    "the paged read must issue a SELECT against Notifications");
                notificationSelects.Should().Contain(
                    sql => sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)
                           && sql.Contains("OFFSET", StringComparison.OrdinalIgnoreCase),
                    "paging must be pushed into SQL (LIMIT + OFFSET), not materialized then sliced in memory");
            }
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                var path = dbPath + suffix;
                if (!File.Exists(path))
                {
                    continue;
                }

                try { File.Delete(path); }
                catch (IOException) { /* best-effort temp cleanup */ }
            }
        }
    }

    /// <summary>
    /// Edge case (#1133 / PR #1171 review, LOW): a negative limit must clamp to an empty result.
    /// SQLite treats a negative LIMIT as "unbounded", so without the clamp a negative page size
    /// would silently restore the unbounded fetch the fix removed.
    /// </summary>
    [Fact]
    public async Task GetByUserIdAsync_WithNegativeLimit_ReturnsEmpty()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var user = new User("notif-neg-user", "notif-neg@example.com", "hash");
        db.Users.Add(user);
        db.Notifications.AddRange(
            new Notification(user.Id, NotificationType.System, NotificationCadence.Immediate, "Neg one", "Msg"),
            new Notification(user.Id, NotificationType.System, NotificationCadence.Immediate, "Neg two", "Msg"));
        await db.SaveChangesAsync();

        var results = (await repo.GetByUserIdAsync(user.Id, limit: -1)).ToList();

        results.Should().BeEmpty();
    }

    /// <summary>
    /// Edge case (#1133 / PR #1171 review, LOW): a zero limit must return no rows.
    /// </summary>
    [Fact]
    public async Task GetByUserIdAsync_WithZeroLimit_ReturnsEmpty()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var user = new User("notif-zero-user", "notif-zero@example.com", "hash");
        db.Users.Add(user);
        db.Notifications.Add(
            new Notification(user.Id, NotificationType.System, NotificationCadence.Immediate, "Zero one", "Msg"));
        await db.SaveChangesAsync();

        var results = (await repo.GetByUserIdAsync(user.Id, limit: 0)).ToList();

        results.Should().BeEmpty();
    }

    /// <summary>
    /// Edge case (#1133 / PR #1171 review, LOW): combining <c>unreadOnly</c> with a <c>boardId</c>
    /// filter must apply BOTH predicates, returning only the unread notification on that board.
    /// </summary>
    [Fact]
    public async Task GetByUserIdAsync_WithUnreadOnlyAndBoardFilter_ReturnsOnlyMatchingUnreadOnBoard()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var user = new User("notif-combo-user", "notif-combo@example.com", "hash");
        db.Users.Add(user);

        var targetBoard = new Board("Combo target board", ownerId: user.Id);
        var otherBoard = new Board("Combo other board", ownerId: user.Id);
        db.Boards.AddRange(targetBoard, otherBoard);

        // Only this one satisfies BOTH unreadOnly AND boardId == targetBoard.
        var unreadOnTarget = new Notification(user.Id, NotificationType.BoardChange, NotificationCadence.Immediate,
            "Unread on target", "Match", boardId: targetBoard.Id);
        // Read on the target board -> excluded by unreadOnly.
        var readOnTarget = new Notification(user.Id, NotificationType.BoardChange, NotificationCadence.Immediate,
            "Read on target", "Excluded by unreadOnly", boardId: targetBoard.Id);
        readOnTarget.MarkAsRead();
        // Unread on a different board -> excluded by the boardId filter.
        var unreadOnOther = new Notification(user.Id, NotificationType.BoardChange, NotificationCadence.Immediate,
            "Unread on other", "Excluded by boardId", boardId: otherBoard.Id);
        // Unread with no board -> excluded by the boardId filter.
        var unreadNoBoard = new Notification(user.Id, NotificationType.System, NotificationCadence.Immediate,
            "Unread no board", "Excluded by boardId");
        db.Notifications.AddRange(unreadOnTarget, readOnTarget, unreadOnOther, unreadNoBoard);
        await db.SaveChangesAsync();

        var results = (await repo.GetByUserIdAsync(user.Id, unreadOnly: true, boardId: targetBoard.Id)).ToList();

        results.Should().ContainSingle().Which.Id.Should().Be(unreadOnTarget.Id);
    }

    /// <summary>
    /// Test-only command interceptor that records the text of every reader command EF executes,
    /// so a test can assert what SQL actually reached SQLite (e.g. that paging carries LIMIT/OFFSET).
    /// </summary>
    private sealed class CapturingCommandInterceptor : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _commands = new();

        public IReadOnlyCollection<string> CapturedCommands => _commands;

        public void Clear() => _commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            _commands.Enqueue(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            _commands.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
