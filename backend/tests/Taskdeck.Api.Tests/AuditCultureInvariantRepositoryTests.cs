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
/// <c>TimeSeparator = "."</c>. The literals use the REAL EF Core SQLite storage shape:
/// <c>yyyy-MM-dd HH:mm:ss.FFFFFFFzzz</c> — trailing fraction zeros are TRIMMED and the dot is
/// dropped entirely at a zero fraction (empirically verified on PR #1391), so whole-second seeds
/// must be written as e.g. <c>2020-06-15 12:00:00+00:00</c> for the tests to exercise the shapes
/// production string comparisons actually run against (the fixed-vs-trimmed boundary residual is
/// tracked as issue #1403). The seeded rows share the hour+minute of the comparison bound so the
/// divergence is forced into the seconds separator, making the assertion fail on the unfixed code.
/// The culture is set before the await chain starts (it flows across awaits via ExecutionContext)
/// and restored in a finally.
///
/// Uses <see cref="HostedWorkerDisabledTestWebApplicationFactory"/> because these tests seed audit
/// rows that <c>AuditRetentionWorker</c> polls and deletes — see that factory's decision rule.
/// Each backdated test uses a distinct year so the table-wide retention delete in one test cannot
/// touch the rows seeded by another (the class shares one database across its methods). The consume
/// test seeds at real current time with a unique code and completes all assertions within itself
/// (a consumed row may later be swept by DeleteExpiredAsync's IsConsumed=1 clause — that is fine).
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
        // string divergence lands in the seconds separator, not an earlier digit. EF-realistic
        // trimmed shape: whole-second values carry NO fraction (see class doc).
        await SetTimestampAsync(db, oldEntry.Id, "2020-06-15 12:00:00+00:00");
        await SetTimestampAsync(db, recentEntry.Id, "2020-06-15 12:00:10+00:00");

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
        // EF-realistic trimmed shape: no fraction on a whole-second value (see class doc).
        await SetTimestampAsync(db, entry.Id, "2021-06-15 12:00:05+00:00");

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
        // the live one follows it, forcing the divergence into the seconds separator. EF-realistic
        // trimmed shape: no fraction on whole-second values (see class doc).
        await SetExpiresAtAsync(db, expiredCode.Id, "2022-06-15 12:00:00+00:00");
        await SetExpiresAtAsync(db, liveCode.Id, "2022-06-15 12:00:10+00:00");

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

    [Fact]
    public async Task TryConsumeAtomicAsync_UnderNonInvariantTimeSeparator_ConsumesAndPersistsInvariantTimestamps()
    {
        // MED-B1 (PR #1400 round 2): TryConsumeAtomicAsync is the live OAuth code-exchange path.
        // Its hand-built nowStr feeds BOTH the ExpiresAt > {now} comparison AND the persisted
        // ConsumedAt/UpdatedAt values — pre-fix, a hostile culture WROTE the malformed string
        // (with '.' time separators) into those columns. This runs the real SQL path.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IOAuthAuthCodeRepository>();

        // Seed THROUGH EF so ExpiresAt is stored in the real trimmed invariant shape.
        var code = $"consume-{Guid.NewGuid():N}";
        var authCode = new OAuthAuthCode(code, Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddMinutes(5));
        db.Set<OAuthAuthCode>().Add(authCode);
        await db.SaveChangesAsync();

        var consumed = await UnderHostileTimeSeparatorCultureAsync(
            () => repo.TryConsumeAtomicAsync(code));

        consumed.Should().BeTrue(
            "an unexpired, unconsumed code must consume successfully under a non-':' culture");

        var reloaded = await db.Set<OAuthAuthCode>().AsNoTracking()
            .SingleAsync(c => c.Id == authCode.Id);
        reloaded.IsConsumed.Should().BeTrue();

        // Assert the raw persisted TEXT, not the materialized DateTimeOffset: a malformed write
        // is only visible at the string level (EF materialization may still parse or throw later).
        var consumedAtRaw = (await db.Database
            .SqlQueryRaw<string>("SELECT ConsumedAt AS Value FROM OAuthAuthCodes WHERE Id = {0}", authCode.Id)
            .ToListAsync()).Single();
        consumedAtRaw.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{7}\+00:00$",
            "ConsumedAt must be persisted in the invariant SQLite timestamp shape with ':' time separators");

        var updatedAtRaw = (await db.Database
            .SqlQueryRaw<string>("SELECT UpdatedAt AS Value FROM OAuthAuthCodes WHERE Id = {0}", authCode.Id)
            .ToListAsync()).Single();
        updatedAtRaw.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{7}\+00:00$",
            "UpdatedAt must be persisted in the invariant SQLite timestamp shape with ':' time separators");
    }

    private static Task SetTimestampAsync(TaskdeckDbContext db, Guid id, string invariantTimestamp)
        => db.Database.ExecuteSqlRawAsync(
            "UPDATE AuditLogs SET Timestamp = {0} WHERE Id = {1}", invariantTimestamp, id);

    private static Task SetExpiresAtAsync(TaskdeckDbContext db, Guid id, string invariantTimestamp)
        => db.Database.ExecuteSqlRawAsync(
            "UPDATE OAuthAuthCodes SET ExpiresAt = {0} WHERE Id = {1}", invariantTimestamp, id);
}
