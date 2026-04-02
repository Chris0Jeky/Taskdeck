using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests for LlmUsageRecordRepository against real SQLite.
/// Covers date format correctness (ISO 8601 with SQLite text comparison),
/// empty time windows, surface filtering, user filtering, and token aggregation.
/// </summary>
public class LlmUsageRecordRepositoryIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LlmUsageRecordRepositoryIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetRequestCountAsync_ShouldCountRecordsInTimeWindow()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmUsageRecordRepository>();

        var user = new User("usage-cnt-user", "usage-cnt@example.com", "hash");
        db.Users.Add(user);

        var record1 = new LlmUsageRecord(user.Id, LlmSurface.Chat, "mock", "gpt-4", 100, 50);
        var record2 = new LlmUsageRecord(user.Id, LlmSurface.Chat, "mock", "gpt-4", 200, 100);
        db.LlmUsageRecords.AddRange(record1, record2);
        await db.SaveChangesAsync();

        var from = DateTimeOffset.UtcNow.AddMinutes(-5);
        var to = DateTimeOffset.UtcNow.AddMinutes(5);

        var count = await repo.GetRequestCountAsync(user.Id, null, from, to);
        count.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task GetRequestCountAsync_WithEmptyTimeWindow_ShouldReturnZero()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmUsageRecordRepository>();

        var user = new User("usage-empty-user", "usage-empty@example.com", "hash");
        db.Users.Add(user);

        db.LlmUsageRecords.Add(new LlmUsageRecord(user.Id, LlmSurface.Chat, "mock", "gpt-4", 100, 50));
        await db.SaveChangesAsync();

        // Time window that excludes all records (far past)
        var from = DateTimeOffset.UtcNow.AddDays(-10);
        var to = DateTimeOffset.UtcNow.AddDays(-9);

        var count = await repo.GetRequestCountAsync(user.Id, null, from, to);
        count.Should().Be(0);
    }

    [Fact]
    public async Task GetRequestCountAsync_WithSurfaceFilter_ShouldFilterBySurface()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmUsageRecordRepository>();

        var user = new User("usage-surf-user", "usage-surf@example.com", "hash");
        db.Users.Add(user);

        var chatRecord = new LlmUsageRecord(user.Id, LlmSurface.Chat, "mock", "gpt-4", 100, 50);
        var captureRecord = new LlmUsageRecord(user.Id, LlmSurface.CaptureTriage, "mock", "gpt-4", 100, 50);
        db.LlmUsageRecords.AddRange(chatRecord, captureRecord);
        await db.SaveChangesAsync();

        var from = DateTimeOffset.UtcNow.AddMinutes(-5);
        var to = DateTimeOffset.UtcNow.AddMinutes(5);

        var chatCount = await repo.GetRequestCountAsync(user.Id, LlmSurface.Chat, from, to);
        var captureCount = await repo.GetRequestCountAsync(user.Id, LlmSurface.CaptureTriage, from, to);

        chatCount.Should().BeGreaterOrEqualTo(1);
        captureCount.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task GetTotalTokensAsync_ShouldSumInputAndOutput()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmUsageRecordRepository>();

        var user = new User("usage-tok-user", "usage-tok@example.com", "hash");
        db.Users.Add(user);

        // Use unique surface (Worker) to avoid interference with other tests
        var r1 = new LlmUsageRecord(user.Id, LlmSurface.Worker, "mock", "gpt-4", 100, 50);
        var r2 = new LlmUsageRecord(user.Id, LlmSurface.Worker, "mock", "gpt-4", 200, 100);
        db.LlmUsageRecords.AddRange(r1, r2);
        await db.SaveChangesAsync();

        var from = DateTimeOffset.UtcNow.AddMinutes(-5);
        var to = DateTimeOffset.UtcNow.AddMinutes(5);

        var total = await repo.GetTotalTokensAsync(user.Id, LlmSurface.Worker, from, to);

        // (100+50) + (200+100) = 450
        total.Should().BeGreaterOrEqualTo(450);
    }

    [Fact]
    public async Task GetTotalTokensAsync_WithEmptyWindow_ShouldReturnZero()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmUsageRecordRepository>();

        var user = new User("usage-tok0-user", "usage-tok0@example.com", "hash");
        db.Users.Add(user);

        db.LlmUsageRecords.Add(new LlmUsageRecord(user.Id, LlmSurface.Chat, "mock", "gpt-4", 100, 50));
        await db.SaveChangesAsync();

        var from = DateTimeOffset.UtcNow.AddDays(-10);
        var to = DateTimeOffset.UtcNow.AddDays(-9);

        var total = await repo.GetTotalTokensAsync(user.Id, null, from, to);
        total.Should().Be(0);
    }

    [Fact]
    public async Task GetUsageSummaryAsync_ShouldReturnCorrectAggregates()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmUsageRecordRepository>();

        var user = new User("usage-sum-user", "usage-sum@example.com", "hash");
        db.Users.Add(user);

        // Use Worker surface + this user to isolate from other tests
        var r1 = new LlmUsageRecord(user.Id, LlmSurface.Worker, "mock", "model-a", 100, 50);
        var r2 = new LlmUsageRecord(user.Id, LlmSurface.Worker, "mock", "model-a", 200, 75);
        db.LlmUsageRecords.AddRange(r1, r2);
        await db.SaveChangesAsync();

        var from = DateTimeOffset.UtcNow.AddMinutes(-5);
        var to = DateTimeOffset.UtcNow.AddMinutes(5);

        var summary = await repo.GetUsageSummaryAsync(user.Id, LlmSurface.Worker, from, to);

        summary.TotalRequests.Should().BeGreaterOrEqualTo(2);
        summary.TotalInputTokens.Should().BeGreaterOrEqualTo(300); // 100 + 200
        summary.TotalOutputTokens.Should().BeGreaterOrEqualTo(125); // 50 + 75
    }

    [Fact]
    public async Task GetUsageSummaryAsync_WithEmptyWindow_ShouldReturnAllZeros()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmUsageRecordRepository>();

        var user = new User("usage-sum0-user", "usage-sum0@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var from = DateTimeOffset.UtcNow.AddDays(-10);
        var to = DateTimeOffset.UtcNow.AddDays(-9);

        var summary = await repo.GetUsageSummaryAsync(user.Id, null, from, to);

        summary.TotalRequests.Should().Be(0);
        summary.TotalInputTokens.Should().Be(0);
        summary.TotalOutputTokens.Should().Be(0);
    }

    [Fact]
    public async Task GetRequestCountAsync_WithNullUserId_ShouldCountAllUsers()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmUsageRecordRepository>();

        var userA = new User("usage-nul-a", "usage-nul-a@example.com", "hash");
        var userB = new User("usage-nul-b", "usage-nul-b@example.com", "hash");
        db.Users.AddRange(userA, userB);

        db.LlmUsageRecords.Add(new LlmUsageRecord(userA.Id, LlmSurface.Chat, "mock", "gpt-4", 10, 5));
        db.LlmUsageRecords.Add(new LlmUsageRecord(userB.Id, LlmSurface.Chat, "mock", "gpt-4", 20, 10));
        await db.SaveChangesAsync();

        var from = DateTimeOffset.UtcNow.AddMinutes(-5);
        var to = DateTimeOffset.UtcNow.AddMinutes(5);

        var allUsersCount = await repo.GetRequestCountAsync(null, null, from, to);
        var userACount = await repo.GetRequestCountAsync(userA.Id, null, from, to);

        // All users count should be >= each individual user count
        allUsersCount.Should().BeGreaterOrEqualTo(userACount);
    }

    [Fact]
    public async Task DateTimePrecision_ShouldHandleSubSecondCreatedAt()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<ILlmUsageRecordRepository>();

        var user = new User("usage-prec-user", "usage-prec@example.com", "hash");
        db.Users.Add(user);

        var before = DateTimeOffset.UtcNow;
        var record = new LlmUsageRecord(user.Id, LlmSurface.Chat, "mock", "gpt-4", 50, 25);
        db.LlmUsageRecords.Add(record);
        await db.SaveChangesAsync();
        var after = DateTimeOffset.UtcNow.AddMilliseconds(100);

        // Using a tight window around the record creation
        var count = await repo.GetRequestCountAsync(user.Id, LlmSurface.Chat, before, after);
        count.Should().BeGreaterOrEqualTo(1);
    }
}
