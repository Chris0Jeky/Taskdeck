using System.Globalization;
using FluentAssertions;
using Taskdeck.Infrastructure.Services;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Regression tests for the read-side of the issue #1393 bug class (PR #1400 round 2, MED-A1):
/// <c>KnowledgeFtsSearchService.MapRowToDto</c> parses the EF-stored invariant SQLite TEXT
/// (<c>yyyy-MM-dd HH:mm:ss.FFFFFFFzzz</c>) for <c>CreatedAt</c>. A culture-sensitive
/// <c>DateTimeOffset.TryParse</c> fails on hosts whose culture cannot parse that shape and
/// silently yields <c>DateTimeOffset.MinValue</c> (wrong ordering/display in FTS results).
///
/// Hostile cultures used:
/// - an invariant clone with <c>TimeSeparator = "."</c> (same probe as
///   <see cref="AuditCultureInvariantRepositoryTests"/>). Empirical note (red/green run on the
///   unfixed code): .NET's parser still accepts ':' on the READ side regardless of the culture's
///   TimeSeparator, so these cases pass either way — kept as documentation of parse robustness,
///   not as the regression carrier.
/// - <c>ar-SA</c> (real-world: its default Um Al-Qura calendar interprets "2026" as a Hijri year
///   outside the supported range, so a culture-sensitive parse of a Gregorian string FAILS).
///   These cases are the proven red/green regression carrier: they fail (MinValue) on the
///   culture-sensitive TryParse and pass with InvariantCulture.
/// Unit-level test via InternalsVisibleTo("Taskdeck.Api.Tests"); the culture is saved and
/// restored in a finally around the call.
/// </summary>
public class KnowledgeFtsSearchServiceCultureTests
{
    private static readonly DateTimeOffset ExpectedFractional =
        new DateTimeOffset(2026, 7, 17, 12, 34, 56, TimeSpan.Zero).AddTicks(1234567);

    private static readonly DateTimeOffset ExpectedWholeSecond =
        new DateTimeOffset(2026, 7, 17, 12, 34, 56, TimeSpan.Zero);

    private static T UnderCulture<T>(CultureInfo culture, Func<T> action)
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static CultureInfo HostileTimeSeparatorClone()
    {
        var hostile = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        hostile.DateTimeFormat.TimeSeparator = ".";
        return hostile;
    }

    private static KnowledgeSearchRow Row(string createdAt) => new()
    {
        DocumentId = Guid.NewGuid().ToString(),
        Title = "title",
        Snippet = "snippet",
        Rank = -1.5,
        CreatedAt = createdAt
    };

    [Theory]
    [InlineData("2026-07-17 12:34:56.1234567+00:00", true)]  // fixed-width fraction (write-path shape)
    [InlineData("2026-07-17 12:34:56+00:00", false)]         // EF-trimmed whole-second shape (PR #1391 evidence)
    public void MapRowToDto_UnderNonInvariantTimeSeparator_ParsesEfStoredCreatedAt(
        string createdAt, bool fractional)
    {
        var dto = UnderCulture(
            HostileTimeSeparatorClone(),
            () => KnowledgeFtsSearchService.MapRowToDto(Row(createdAt)));

        dto.CreatedAt.Should().Be(
            fractional ? ExpectedFractional : ExpectedWholeSecond,
            "CreatedAt must parse the invariant stored shape under a non-':' time-separator culture");
        dto.CreatedAt.Should().NotBe(DateTimeOffset.MinValue,
            "a failed parse silently yields MinValue — the exact regression this guards against");
    }

    [Theory]
    [InlineData("2026-07-17 12:34:56.1234567+00:00", true)]
    [InlineData("2026-07-17 12:34:56+00:00", false)]
    public void MapRowToDto_UnderNonGregorianCalendarCulture_ParsesEfStoredCreatedAt(
        string createdAt, bool fractional)
    {
        var dto = UnderCulture(
            new CultureInfo("ar-SA"),
            () => KnowledgeFtsSearchService.MapRowToDto(Row(createdAt)));

        dto.CreatedAt.Should().Be(
            fractional ? ExpectedFractional : ExpectedWholeSecond,
            "CreatedAt must parse the invariant stored shape under a non-Gregorian-calendar culture");
        dto.CreatedAt.Should().NotBe(DateTimeOffset.MinValue);
    }
}
