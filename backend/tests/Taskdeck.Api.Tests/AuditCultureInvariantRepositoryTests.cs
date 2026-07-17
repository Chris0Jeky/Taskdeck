using System.Globalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Regression tests for issue #1393: production repositories that hand-build SQLite
/// DateTimeOffset comparison strings must format with <see cref="CultureInfo.InvariantCulture"/>.
///
/// The <c>:</c> in a .NET custom date/time format string is the culture-sensitive time separator
/// (<see cref="DateTimeFormatInfo.TimeSeparator"/>). EF Core stores <see cref="DateTimeOffset"/> in
/// SQLite using the invariant <c>yyyy-MM-dd HH:mm:ss.fffffff+HH:mm</c> shape. On a host whose culture
/// uses a non-<c>:</c> time separator the hand-built string diverges from the stored rows and the
/// string-based comparison silently returns the wrong rows.
///
/// Each test seeds rows with LITERAL invariant timestamp strings (culture-immune), then invokes the
/// repository under a hostile culture — a clone of the invariant culture whose only difference is
/// <c>TimeSeparator = "."</c>. The seeded rows share the hour+minute of the comparison bound so the
/// divergence is forced into the seconds separator, making the assertion fail on the unfixed code.
/// The culture is set before the await chain starts (it flows across awaits via ExecutionContext)
/// and restored in a finally.
///
/// Uses <see cref="HostedWorkerDisabledTestWebApplicationFactory"/> because these tests seed audit
/// rows that <c>AuditRetentionWorker</c> polls and deletes — see that factory's decision rule.
/// Each test uses a distinct year so the table-wide retention delete in one test cannot touch the
/// rows seeded by another (the class shares one database across its methods).
/// </summary>
public class AuditCultureInvariantRepositoryTests : IClassFixture<HostedWorkerDisabledTestWebApplicationFactory>
{
    private readonly HostedWorkerDisabledTestWebApplicationFactory _factory;

    public AuditCultureInvariantRepositoryTests(HostedWorkerDisabledTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static async Task<T> UnderHostileTimeSeparatorCultureAsync<T>(Func<Task<T>> action)
    {
        // Clone the invariant culture and change ONLY the time separator, isolating the exact bug:
        // everything else (date separator '-', digit grouping) stays invariant.
        var hostile = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        hostile.DateTimeFormat.TimeSeparator = ".";

        var original = CultureInfo.CurrentCulture;
        // Set before the await chain starts; CurrentCulture flows across awaits via ExecutionContext.
        CultureInfo.CurrentCulture = hostile;
        try
        {
            return await action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task DeleteOldEntriesAsync_UnderNonInvariantTimeSeparator_DeletesCorrectEntries()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var oldEntry = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created);
        var recentEntry = new AuditLog("Board", Guid.NewGuid(), AuditAction.Created);
        db.AuditLogs.AddRange(oldEntry, recentEntry);
        await db.SaveChangesAsync();

        // Literal invariant timestamps sharing 2020-06-15 12:00 with the cutoff (12:00:05) so the
        // string divergence lands in the seconds separator, not an earlier digit.
        await SetTimestampAsync(db, oldEntry.Id, "2020-06-15 12:00:00.0000000+00:00");
        await SetTimestampAsync(db, recentEntry.Id, "2020-06-15 12:00:10.0000000+00:00");

        var cutoff = new DateTimeOffset(2020, 6, 15, 12, 0, 5, TimeSpan.Zero);

        var deleted = await UnderHostileTimeSeparatorCultureAsync(
            () => repo.DeleteOldEntriesAsync(cutoff, batchSize: 1000));

        deleted.Should().BeGreaterThanOrEqualTo(1,
            "the entry older than the cutoff must be deleted even under a non-':' time-separator culture");

        // Verify against the database (AsNoTracking) rather than GetByIdAsync: the raw-SQL DELETE
        // bypasses the change tracker, so a Find-based read would return the still-tracked instance.
        var survivors = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Id == oldEntry.Id || a.Id == recentEntry.Id)
            .Select(a => a.Id)
            .ToListAsync();
        survivors.Should().NotContain(oldEntry.Id,
            "the pre-cutoff entry must be deleted regardless of host culture");
        survivors.Should().Contain(recentEntry.Id,
            "the post-cutoff entry must survive regardless of host culture");
    }

    [Fact]
    public async Task CountByDateAsync_UnderNonInvariantTimeSeparator_CountsBoundaryRow()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        // AuditLog.UserId is a real FK to Users, so seed a user to own the row.
        var user = new User("audit-culture-count", "audit-culture-count@example.com", "hash");
        db.Users.Add(user);

        var entry = new AuditLog("Card", Guid.NewGuid(), AuditAction.Updated, user.Id);
        db.AuditLogs.Add(entry);
        await db.SaveChangesAsync();

        // Row at 2021-06-15 12:00:05; range bounds share the hour+minute (12:00:00..12:00:10) so a
        // corrupted time separator on either bound would wrongly exclude the row at the range edge.
        await SetTimestampAsync(db, entry.Id, "2021-06-15 12:00:05.0000000+00:00");

        var from = new DateTimeOffset(2021, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2021, 6, 15, 12, 0, 10, TimeSpan.Zero);

        var counts = await UnderHostileTimeSeparatorCultureAsync(
            () => repo.CountByDateAsync(from, to, user.Id));

        counts.Should().ContainSingle(
            "the seeded row lies inside the range and must be counted under a non-':' culture");
        counts[0].Date.Should().Be(new DateOnly(2021, 6, 15));
        counts[0].Count.Should().Be(1);
    }

    [Fact]
    public async Task DeleteExpiredAsync_UnderNonInvariantTimeSeparator_DeletesExpiredCode()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IOAuthAuthCodeRepository>();

        // Construct with a valid future expiry (entity invariant), then backdate via raw SQL.
        var expiredCode = new OAuthAuthCode(
            $"expired-{Guid.NewGuid():N}", Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddMinutes(5));
        var liveCode = new OAuthAuthCode(
            $"live-{Guid.NewGuid():N}", Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddMinutes(5));
        db.Set<OAuthAuthCode>().AddRange(expiredCode, liveCode);
        await db.SaveChangesAsync();

        // Both codes share 2022-06-15 12:00 with the cutoff (12:00:05); the expired one precedes it,
        // the live one follows it, forcing the divergence into the seconds separator.
        await SetExpiresAtAsync(db, expiredCode.Id, "2022-06-15 12:00:00.0000000+00:00");
        await SetExpiresAtAsync(db, liveCode.Id, "2022-06-15 12:00:10.0000000+00:00");

        var cutoff = new DateTimeOffset(2022, 6, 15, 12, 0, 5, TimeSpan.Zero);

        var deleted = await UnderHostileTimeSeparatorCultureAsync(
            () => repo.DeleteExpiredAsync(cutoff));

        deleted.Should().BeGreaterThanOrEqualTo(1,
            "the code expiring before the cutoff must be deleted even under a non-':' culture");

        var remaining = await db.Set<OAuthAuthCode>().AsNoTracking()
            .Where(c => c.Id == expiredCode.Id || c.Id == liveCode.Id)
            .Select(c => c.Id)
            .ToListAsync();
        remaining.Should().NotContain(expiredCode.Id, "the pre-cutoff code must be deleted");
        remaining.Should().Contain(liveCode.Id, "the post-cutoff code must survive");
    }

    private static Task SetTimestampAsync(TaskdeckDbContext db, Guid id, string invariantTimestamp)
        => db.Database.ExecuteSqlRawAsync(
            "UPDATE AuditLogs SET Timestamp = {0} WHERE Id = {1}", invariantTimestamp, id);

    private static Task SetExpiresAtAsync(TaskdeckDbContext db, Guid id, string invariantTimestamp)
        => db.Database.ExecuteSqlRawAsync(
            "UPDATE OAuthAuthCodes SET ExpiresAt = {0} WHERE Id = {1}", invariantTimestamp, id);
}
