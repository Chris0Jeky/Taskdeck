using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class DailySnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    #region Construction

    [Fact]
    public void Constructor_ValidInputs_CreatesUnsealedSnapshot()
    {
        var userId = Guid.NewGuid();

        var snapshot = new DailySnapshot(userId, Today, Now);

        snapshot.UserId.Should().Be(userId);
        snapshot.Date.Should().Be(Today);
        snapshot.IsSealed.Should().BeFalse();
        snapshot.SealedAt.Should().BeNull();
        snapshot.Id.Should().NotBe(Guid.Empty);
        snapshot.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Constructor_EmptyUserId_ThrowsDomainException()
    {
        var act = () => new DailySnapshot(Guid.Empty, Today, Now);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError)
            .WithMessage("*UserId*");
    }

    [Fact]
    public void Constructor_FutureDate_ThrowsDomainException()
    {
        var futureDate = Today.AddDays(1);

        var act = () => new DailySnapshot(Guid.NewGuid(), futureDate, Now);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError)
            .WithMessage("*future*");
    }

    [Fact]
    public void Constructor_TodayDate_Succeeds()
    {
        var snapshot = new DailySnapshot(Guid.NewGuid(), Today, Now);

        snapshot.Date.Should().Be(Today);
    }

    [Fact]
    public void Constructor_PastDate_Succeeds()
    {
        var pastDate = Today.AddDays(-7);

        var snapshot = new DailySnapshot(Guid.NewGuid(), pastDate, Now);

        snapshot.Date.Should().Be(pastDate);
    }

    #endregion

    #region Seal

    [Fact]
    public void Seal_UnsealedSnapshot_SetsSealedAt()
    {
        var snapshot = new DailySnapshot(Guid.NewGuid(), Today, Now);

        snapshot.Seal(Now);

        snapshot.IsSealed.Should().BeTrue();
        snapshot.SealedAt.Should().Be(Now);
    }

    [Fact]
    public void Seal_AlreadySealedSnapshot_IsNoOp()
    {
        var snapshot = new DailySnapshot(Guid.NewGuid(), Today, Now);
        snapshot.Seal(Now);
        var originalSealedAt = snapshot.SealedAt;

        var laterTime = Now.AddHours(2);
        snapshot.Seal(laterTime);

        snapshot.SealedAt.Should().Be(originalSealedAt);
        snapshot.IsSealed.Should().BeTrue();
    }

    [Fact]
    public void Seal_MultipleTimes_PreservesOriginalTimestamp()
    {
        var snapshot = new DailySnapshot(Guid.NewGuid(), Today, Now);
        snapshot.Seal(Now);

        for (int i = 1; i <= 5; i++)
        {
            snapshot.Seal(Now.AddMinutes(i));
        }

        snapshot.SealedAt.Should().Be(Now);
    }

    #endregion

    #region IsSealed

    [Fact]
    public void IsSealed_NewSnapshot_ReturnsFalse()
    {
        var snapshot = new DailySnapshot(Guid.NewGuid(), Today, Now);

        snapshot.IsSealed.Should().BeFalse();
    }

    [Fact]
    public void IsSealed_SealedSnapshot_ReturnsTrue()
    {
        var snapshot = new DailySnapshot(Guid.NewGuid(), Today, Now);
        snapshot.Seal(Now);

        snapshot.IsSealed.Should().BeTrue();
    }

    #endregion
}
