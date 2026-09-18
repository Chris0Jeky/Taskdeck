using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Proves that caller-supplied offsets are normalized before SQLite TEXT comparisons and writes.
/// SQLite orders these timestamps lexically, so equivalent instants with different offsets cannot
/// be bound directly without changing the selected rows.
/// </summary>
public class LlmUsageRecordUtcNormalizationIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LlmUsageRecordUtcNormalizationIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetRequestCountAsync_WithNonUtcBounds_SelectsTheSameInstantWindow()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<ILlmUsageRecordRepository>();
        var user = CreateUniqueUser("usage-window-offset");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var storedAtUtc = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);
        await SeedCommittedUsageAtAsync(db, user.Id, LlmSurface.Chat, storedAtUtc);
        var callerOffset = TimeSpan.FromHours(2);

        var count = await repository.GetRequestCountAsync(
            user.Id,
            LlmSurface.Chat,
            storedAtUtc.AddMinutes(-1).ToOffset(callerOffset),
            storedAtUtc.AddMinutes(1).ToOffset(callerOffset));

        count.Should().Be(1);
    }

    [Fact]
    public async Task TryReserveAsync_WithNonUtcBounds_AppliesTheRequestLimitByInstant()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<ILlmUsageRecordRepository>();
        var user = CreateUniqueUser("usage-reserve-offset-limit");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var nowUtc = new DateTimeOffset(2026, 1, 15, 10, 40, 0, TimeSpan.Zero);
        await SeedCommittedUsageAtAsync(
            db,
            user.Id,
            LlmSurface.Worker,
            nowUtc.AddMinutes(-10));

        var callerOffset = TimeSpan.FromHours(2);
        var dayStartUtc = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var outcome = await repository.TryReserveAsync(
            user.Id,
            LlmSurface.Worker,
            nowUtc.AddHours(-1).ToOffset(callerOffset),
            nowUtc.ToOffset(callerOffset),
            dayStartUtc.ToOffset(callerOffset),
            dayStartUtc.AddDays(1).ToOffset(callerOffset),
            requestsPerHour: 1,
            tokensPerDay: 0,
            globalBudgetCeilingTokens: 0,
            estimatedTokens: 25,
            expiresAt: nowUtc.AddMinutes(5).ToOffset(callerOffset));

        outcome.Decision.Should().Be(QuotaReservationDecision.RequestsExceeded);
        outcome.ReservationId.Should().BeNull();
        outcome.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task TryReserveAsync_WithNonUtcTimestamps_PersistsTheReservationInUtc()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<ILlmUsageRecordRepository>();
        var user = CreateUniqueUser("usage-reserve-offset-write");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var nowUtc = new DateTimeOffset(2026, 1, 15, 10, 40, 0, TimeSpan.Zero);
        var expiresAtUtc = nowUtc.AddMinutes(5);
        var dayStartUtc = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var callerOffset = TimeSpan.FromHours(5.5);

        var outcome = await repository.TryReserveAsync(
            user.Id,
            LlmSurface.CaptureTriage,
            nowUtc.AddHours(-1).ToOffset(callerOffset),
            nowUtc.ToOffset(callerOffset),
            dayStartUtc.ToOffset(callerOffset),
            dayStartUtc.AddDays(1).ToOffset(callerOffset),
            requestsPerHour: 0,
            tokensPerDay: 0,
            globalBudgetCeilingTokens: 0,
            estimatedTokens: 25,
            expiresAt: expiresAtUtc.ToOffset(callerOffset));

        outcome.Decision.Should().Be(QuotaReservationDecision.Allowed);
        outcome.ReservationId.Should().NotBeNull();

        db.ChangeTracker.Clear();
        var reservation = await db.LlmUsageRecords
            .AsNoTracking()
            .SingleAsync(record => record.Id == outcome.ReservationId!.Value);

        reservation.CreatedAt.Should().Be(nowUtc);
        reservation.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        reservation.UpdatedAt.Should().Be(nowUtc);
        reservation.UpdatedAt.Offset.Should().Be(TimeSpan.Zero);
        reservation.ExpiresAt.Should().Be(expiresAtUtc);
        reservation.ExpiresAt!.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    private static User CreateUniqueUser(string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new User($"{prefix}-{suffix[..12]}", $"{suffix}@example.com", "hash");
    }

    private static async Task SeedCommittedUsageAtAsync(
        TaskdeckDbContext db,
        Guid userId,
        LlmSurface surface,
        DateTimeOffset createdAtUtc)
    {
        var record = new LlmUsageRecord(userId, surface, "mock", "model", 10, 5);
        db.LlmUsageRecords.Add(record);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE LlmUsageRecords SET CreatedAt = {createdAtUtc}, UpdatedAt = {createdAtUtc} WHERE Id = {record.Id}");
        db.ChangeTracker.Clear();
    }
}
