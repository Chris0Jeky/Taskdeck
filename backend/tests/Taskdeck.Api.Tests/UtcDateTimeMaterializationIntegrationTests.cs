using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Pins the global UTC DateTime materialization convention configured in
/// <see cref="TaskdeckDbContext.ConfigureConventions"/>.
///
/// SQLite stores DateTime as TEXT without timezone info and EF Core reads it back as
/// DateTimeKind.Unspecified. Without the convention, an Unspecified ExpiresAt compared against
/// a UTC DateTimeOffset / DateTime.UtcNow drifts by the local UTC offset -- the #1191 symptom.
///
/// These tests round-trip an AutomationProposal through real SQLite and assert that materialized
/// DateTime / DateTime? properties carry DateTimeKind.Utc, that a DateTimeOffset comparison behaves
/// correctly, and that a Local-kind value is normalized to UTC on write.
///
/// Deleting the ConfigureConventions override (or reverting the converters to identity) must make
/// these tests FAIL. See https://github.com/Chris0Jeky/Taskdeck/issues/1191.
/// </summary>
public class UtcDateTimeMaterializationIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public UtcDateTimeMaterializationIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RoundTrip_MaterializesDateTimePropertiesAsUtc()
    {
        Guid proposalId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

            var user = new User($"utc-kind-{Guid.NewGuid():N}", $"utc-kind-{Guid.NewGuid():N}@example.com", "hash");
            db.Users.Add(user);

            var proposal = new AutomationProposal(
                ProposalSourceType.Queue, user.Id, "UTC kind round-trip", RiskLevel.Low,
                $"corr-utc-kind-{Guid.NewGuid():N}", expiryMinutes: 60);
            // Approve so DecidedAt (a DateTime?) is populated -- exercises both the value and the
            // nullable converter on materialization.
            proposal.Approve(user.Id);
            proposalId = proposal.Id;

            db.AutomationProposals.Add(proposal);
            await db.SaveChangesAsync();
        }

        // Fresh scope + new DbContext: forces a real materialization from SQLite TEXT,
        // not a tracked-entity replay that would keep the in-memory Utc kind.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

            var loaded = await db.AutomationProposals
                .AsNoTracking()
                .SingleAsync(p => p.Id == proposalId);

            // Non-nullable DateTime property.
            loaded.ExpiresAt.Kind.Should().Be(
                DateTimeKind.Utc,
                "ExpiresAt must materialize as UTC -- without the convention it is Unspecified");

            // Nullable DateTime? property.
            loaded.DecidedAt.Should().NotBeNull();
            loaded.DecidedAt!.Value.Kind.Should().Be(
                DateTimeKind.Utc,
                "DecidedAt (DateTime?) must materialize as UTC via the nullable converter");
        }
    }

    [Fact]
    public async Task RoundTrip_ExpiresAt_ComparesCorrectlyAgainstUtcDateTimeOffset()
    {
        // The #1191 symptom: a future ExpiresAt read back as Unspecified is interpreted in local
        // time. East of UTC (positive offset) that reads as EARLIER than the true instant, so a
        // proposal that is genuinely still valid can compare as already expired. Pin the corrected
        // behavior: a materialized future ExpiresAt is strictly after "now" expressed as a UTC
        // DateTimeOffset, and a past ExpiresAt is strictly before it.
        Guid futureId;
        Guid pastId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

            var user = new User($"utc-cmp-{Guid.NewGuid():N}", $"utc-cmp-{Guid.NewGuid():N}@example.com", "hash");
            db.Users.Add(user);

            var future = new AutomationProposal(
                ProposalSourceType.Queue, user.Id, "Future expiry", RiskLevel.Low,
                $"corr-future-{Guid.NewGuid():N}", expiryMinutes: 120);
            futureId = future.Id;

            var past = new AutomationProposal(
                ProposalSourceType.Queue, user.Id, "Past expiry", RiskLevel.Low,
                $"corr-past-{Guid.NewGuid():N}", expiryMinutes: 1);
            SetExpiresAt(past, DateTime.UtcNow.AddHours(-2));
            pastId = past.Id;

            db.AutomationProposals.AddRange(future, past);
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

            var future = await db.AutomationProposals.AsNoTracking().SingleAsync(p => p.Id == futureId);
            var past = await db.AutomationProposals.AsNoTracking().SingleAsync(p => p.Id == pastId);

            // Convert the materialized UTC DateTime to a DateTimeOffset and compare against the
            // current instant as a UTC DateTimeOffset. With the convention this conversion is exact;
            // without it the Unspecified-as-local interpretation skews the offset.
            var nowOffset = DateTimeOffset.UtcNow;

            new DateTimeOffset(future.ExpiresAt).Should().BeAfter(
                nowOffset.UtcDateTime - TimeSpan.FromSeconds(1),
                "a 2-hour-future ExpiresAt must convert to a DateTimeOffset strictly after now");

            new DateTimeOffset(past.ExpiresAt).Should().BeBefore(
                nowOffset.UtcDateTime,
                "a past ExpiresAt must convert to a DateTimeOffset before now");

            // Domain guard exercises the same comparison (DateTime.UtcNow > ExpiresAt).
            future.IsExpired.Should().BeFalse("future expiry must not read as expired after round-trip");
            past.IsExpired.Should().BeTrue("past expiry must read as expired after round-trip");
        }
    }

    [Fact]
    public async Task Write_NormalizesLocalKindToUtc()
    {
        // The write-side converter normalizes a Local-kind DateTime to UTC so it is not stored as
        // local wall-time and re-read stamped Utc. Force a Local-kind ExpiresAt (no current writer
        // does this -- they all use DateTime.UtcNow) and assert the persisted instant equals the
        // UTC instant of that local time, not its raw wall-clock numbers.
        Guid proposalId;
        DateTime localExpiry;
        DateTime expectedUtc;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

            var user = new User($"utc-write-{Guid.NewGuid():N}", $"utc-write-{Guid.NewGuid():N}@example.com", "hash");
            db.Users.Add(user);

            var proposal = new AutomationProposal(
                ProposalSourceType.Queue, user.Id, "Local-kind write", RiskLevel.Low,
                $"corr-local-{Guid.NewGuid():N}", expiryMinutes: 60);
            proposalId = proposal.Id;

            // A Local-kind instant two hours out, expressed in the host's local zone.
            localExpiry = DateTime.SpecifyKind(DateTime.Now.AddHours(2), DateTimeKind.Local);
            expectedUtc = localExpiry.ToUniversalTime();
            SetExpiresAt(proposal, localExpiry);

            db.AutomationProposals.Add(proposal);
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

            var loaded = await db.AutomationProposals
                .AsNoTracking()
                .SingleAsync(p => p.Id == proposalId);

            loaded.ExpiresAt.Kind.Should().Be(DateTimeKind.Utc);
            // The persisted instant must equal the UTC instant of the local time. If the write
            // converter stored the raw local wall-time, the read-side Utc stamp would make this
            // off by the local UTC offset.
            loaded.ExpiresAt.Should().BeCloseTo(
                DateTime.SpecifyKind(expectedUtc, DateTimeKind.Utc),
                TimeSpan.FromMilliseconds(50),
                "a Local-kind write must be normalized to its UTC instant, not stored as wall-time");
        }
    }

    private static void SetExpiresAt(AutomationProposal proposal, DateTime expiresAt)
    {
        typeof(AutomationProposal)
            .GetProperty(nameof(AutomationProposal.ExpiresAt))!
            .SetValue(proposal, expiresAt);
    }
}
