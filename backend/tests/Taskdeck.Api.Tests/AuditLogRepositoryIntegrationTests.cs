using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests for AuditLogRepository against real SQLite.
/// Covers cross-user isolation, unknown level handling, board-scoped queries,
/// time-range filtering, and entity-type case-insensitivity.
/// </summary>
public class AuditLogRepositoryIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AuditLogRepositoryIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetByUserAsync_ShouldReturnOnlyUserLogs_CrossUserIsolation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var userA = new User("audit-usera", "audit-usera@example.com", "hash");
        var userB = new User("audit-userb", "audit-userb@example.com", "hash");
        db.Users.AddRange(userA, userB);

        var logA = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created, userA.Id);
        var logB = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created, userB.Id);
        db.AuditLogs.AddRange(logA, logB);
        await db.SaveChangesAsync();

        var resultsA = (await repo.GetByUserAsync(userA.Id)).ToList();
        var resultsB = (await repo.GetByUserAsync(userB.Id)).ToList();

        resultsA.Should().Contain(l => l.Id == logA.Id);
        resultsA.Should().NotContain(l => l.Id == logB.Id);
        resultsB.Should().Contain(l => l.Id == logB.Id);
        resultsB.Should().NotContain(l => l.Id == logA.Id);
    }

    [Fact]
    public async Task GetByEntityAsync_ShouldBeCaseInsensitiveOnEntityType()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var entityId = Guid.NewGuid();
        var log = new AuditLog("Board", entityId, AuditAction.Updated);
        db.AuditLogs.Add(log);
        await db.SaveChangesAsync();

        // Query with different casing
        var upper = (await repo.GetByEntityAsync("BOARD", entityId)).ToList();
        var lower = (await repo.GetByEntityAsync("board", entityId)).ToList();
        var mixed = (await repo.GetByEntityAsync("Board", entityId)).ToList();

        upper.Should().Contain(l => l.Id == log.Id);
        lower.Should().Contain(l => l.Id == log.Id);
        mixed.Should().Contain(l => l.Id == log.Id);
    }

    [Fact]
    public async Task QueryAsync_WithUnknownLevel_ShouldReturnEmpty()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var user = new User("audit-unknown-user", "audit-unknown@example.com", "hash");
        db.Users.Add(user);

        var log = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created, user.Id);
        db.AuditLogs.Add(log);
        await db.SaveChangesAsync();

        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddHours(1);

        // "critical" is not a known level ("info" and "warning" are)
        var results = (await repo.QueryAsync(from, to, level: "critical")).ToList();
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_WithInfoLevel_ShouldReturnOnlyInfoActions()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var user = new User("audit-info-user", "audit-info@example.com", "hash");
        db.Users.Add(user);

        var created = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created, user.Id);
        var deleted = new AuditLog("Board", Guid.NewGuid(), AuditAction.Deleted, user.Id);
        db.AuditLogs.AddRange(created, deleted);
        await db.SaveChangesAsync();

        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddHours(1);

        var results = (await repo.QueryAsync(from, to, userId: user.Id, level: "info")).ToList();

        results.Should().Contain(l => l.Id == created.Id);
        results.Should().NotContain(l => l.Id == deleted.Id);
    }

    [Fact]
    public async Task QueryAsync_WithWarningLevel_ShouldReturnOnlyWarningActions()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var user = new User("audit-warn-user", "audit-warn@example.com", "hash");
        db.Users.Add(user);

        var created = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created, user.Id);
        var archived = new AuditLog("Board", Guid.NewGuid(), AuditAction.Archived, user.Id);
        db.AuditLogs.AddRange(created, archived);
        await db.SaveChangesAsync();

        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddHours(1);

        var results = (await repo.QueryAsync(from, to, userId: user.Id, level: "warning")).ToList();

        results.Should().Contain(l => l.Id == archived.Id);
        results.Should().NotContain(l => l.Id == created.Id);
    }

    [Fact]
    public async Task QueryAsync_WithTimeRange_ShouldFilterCorrectly()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var user = new User("audit-time-user", "audit-time@example.com", "hash");
        db.Users.Add(user);

        var log = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created, user.Id);
        db.AuditLogs.Add(log);
        await db.SaveChangesAsync();

        // Time range that includes the log
        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddHours(1);
        var included = (await repo.QueryAsync(from, to, userId: user.Id)).ToList();
        included.Should().Contain(l => l.Id == log.Id);

        // Time range in the past that excludes the log
        var pastFrom = DateTimeOffset.UtcNow.AddDays(-10);
        var pastTo = DateTimeOffset.UtcNow.AddDays(-9);
        var excluded = (await repo.QueryAsync(pastFrom, pastTo, userId: user.Id)).ToList();
        excluded.Should().NotContain(l => l.Id == log.Id);
    }

    [Fact]
    public async Task QueryAsync_WithSourceFilter_ShouldMatchEntityType()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var user = new User("audit-source-user", "audit-source@example.com", "hash");
        db.Users.Add(user);

        var boardLog = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created, user.Id);
        var cardLog = new AuditLog("Card", Guid.NewGuid(), AuditAction.Created, user.Id);
        db.AuditLogs.AddRange(boardLog, cardLog);
        await db.SaveChangesAsync();

        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddHours(1);

        var boardResults = (await repo.QueryAsync(from, to, userId: user.Id, source: "Board")).ToList();

        boardResults.Should().Contain(l => l.Id == boardLog.Id);
        boardResults.Should().NotContain(l => l.Id == cardLog.Id);
    }

    [Fact]
    public async Task GetByBoardAsync_ShouldIncludeColumnAndCardLogs()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var user = new User("audit-brd-user", "audit-brd@example.com", "hash");
        db.Users.Add(user);

        var board = new Board("Audit board test", ownerId: user.Id);
        db.Boards.Add(board);

        var column = new Column(board.Id, "Todo", 0);
        db.Columns.Add(column);

        var card = new Card(board.Id, column.Id, "Test card");
        db.Cards.Add(card);
        await db.SaveChangesAsync();

        // Log for board itself
        var boardLog = new AuditLog("Board", board.Id, AuditAction.Created, user.Id);
        // Log for a column in the board
        var columnLog = new AuditLog("Column", column.Id, AuditAction.Created, user.Id);
        // Log for a card in the board
        var cardLog = new AuditLog("Card", card.Id, AuditAction.Created, user.Id);
        // Log for unrelated entity
        var unrelatedLog = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created, user.Id);

        db.AuditLogs.AddRange(boardLog, columnLog, cardLog, unrelatedLog);
        await db.SaveChangesAsync();

        var results = (await repo.GetByBoardAsync(board.Id)).ToList();

        results.Should().Contain(l => l.Id == boardLog.Id);
        results.Should().Contain(l => l.Id == columnLog.Id);
        results.Should().Contain(l => l.Id == cardLog.Id);
        results.Should().NotContain(l => l.Id == unrelatedLog.Id);
    }

    [Fact]
    public async Task GetByUserAsync_ShouldRespectLimit()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var user = new User("audit-lim-user", "audit-lim@example.com", "hash");
        db.Users.Add(user);

        for (var i = 0; i < 5; i++)
        {
            db.AuditLogs.Add(new AuditLog("Board", Guid.NewGuid(), AuditAction.Created, user.Id));
        }
        await db.SaveChangesAsync();

        var results = (await repo.GetByUserAsync(user.Id, limit: 2)).ToList();

        results.Count.Should().BeLessOrEqualTo(2);
    }

    [Fact]
    public async Task GetByUserAsync_ShouldOrderByTimestampDesc()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var user = new User("audit-order-user", "audit-order@example.com", "hash");
        db.Users.Add(user);

        var first = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created, user.Id);
        await Task.Delay(20);
        var second = new AuditLog("Board", Guid.NewGuid(), AuditAction.Updated, user.Id);
        db.AuditLogs.AddRange(first, second);
        await db.SaveChangesAsync();

        var results = (await repo.GetByUserAsync(user.Id)).ToList();

        var firstIdx = results.FindIndex(l => l.Id == first.Id);
        var secondIdx = results.FindIndex(l => l.Id == second.Id);

        // DESC order: second should appear before first
        if (firstIdx >= 0 && secondIdx >= 0)
        {
            secondIdx.Should().BeLessThan(firstIdx);
        }
    }
}
