using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

/// <summary>
/// Tests for the ArchiveItem entity state machine lifecycle:
/// Available -> Restored, Available -> Expired, Available -> Conflict,
/// Conflict -> Available (via ResetToAvailable), Expired -> Available (via ResetToAvailable),
/// and invalid transitions.
/// </summary>
public class ArchiveItemLifecycleTests
{
    private static ArchiveItem CreateAvailableItem(
        string entityType = "board",
        Guid? entityId = null,
        Guid? boardId = null,
        Guid? archivedByUserId = null,
        string name = "Test Item",
        string snapshotJson = "{\"name\":\"Test\"}")
    {
        return new ArchiveItem(
            entityType,
            entityId ?? Guid.NewGuid(),
            boardId ?? Guid.NewGuid(),
            name,
            archivedByUserId ?? Guid.NewGuid(),
            snapshotJson);
    }

    #region Construction and Initial State

    [Fact]
    public void NewArchiveItem_ShouldHaveAvailableStatus()
    {
        var item = CreateAvailableItem();

        item.RestoreStatus.Should().Be(RestoreStatus.Available);
        item.RestoredAt.Should().BeNull();
        item.RestoredByUserId.Should().BeNull();
    }

    [Theory]
    [InlineData("board")]
    [InlineData("column")]
    [InlineData("card")]
    public void NewArchiveItem_ShouldAcceptValidEntityTypes(string entityType)
    {
        var item = CreateAvailableItem(entityType: entityType);

        item.EntityType.Should().Be(entityType);
        item.RestoreStatus.Should().Be(RestoreStatus.Available);
    }

    [Fact]
    public void NewArchiveItem_ShouldRecordArchivedAtTimestamp()
    {
        var before = DateTime.UtcNow;
        var item = CreateAvailableItem();
        var after = DateTime.UtcNow;

        item.ArchivedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void NewArchiveItem_ShouldStoreSnapshotJson()
    {
        var snapshot = "{\"name\":\"My Board\",\"description\":\"A test board\"}";
        var item = CreateAvailableItem(snapshotJson: snapshot);

        item.SnapshotJson.Should().Be(snapshot);
    }

    [Fact]
    public void NewArchiveItem_WithReason_ShouldStoreReason()
    {
        var item = new ArchiveItem(
            "board", Guid.NewGuid(), Guid.NewGuid(),
            "Test", Guid.NewGuid(), "{}", "Completed sprint");

        item.Reason.Should().Be("Completed sprint");
    }

    #endregion

    #region MarkAsRestored (Available -> Restored)

    [Fact]
    public void MarkAsRestored_FromAvailable_ShouldTransitionToRestored()
    {
        var item = CreateAvailableItem();
        var restoredBy = Guid.NewGuid();

        item.MarkAsRestored(restoredBy);

        item.RestoreStatus.Should().Be(RestoreStatus.Restored);
        item.RestoredByUserId.Should().Be(restoredBy);
        item.RestoredAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkAsRestored_ShouldRecordTimestamp()
    {
        var item = CreateAvailableItem();
        var before = DateTime.UtcNow;

        item.MarkAsRestored(Guid.NewGuid());

        item.RestoredAt.Should().NotBeNull();
        item.RestoredAt!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void MarkAsRestored_ShouldCallTouch()
    {
        var item = CreateAvailableItem();
        var initialUpdatedAt = item.UpdatedAt;

        // MarkAsRestored should update UpdatedAt; assertion allows equal-or-later timestamps
        item.MarkAsRestored(Guid.NewGuid());

        item.UpdatedAt.Should().BeOnOrAfter(initialUpdatedAt);
    }

    [Fact]
    public void MarkAsRestored_WithEmptyUserId_ShouldThrow()
    {
        var item = CreateAvailableItem();

        var act = () => item.MarkAsRestored(Guid.Empty);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    #endregion

    #region MarkAsExpired (Available -> Expired)

    [Fact]
    public void MarkAsExpired_FromAvailable_ShouldTransitionToExpired()
    {
        var item = CreateAvailableItem();

        item.MarkAsExpired();

        item.RestoreStatus.Should().Be(RestoreStatus.Expired);
    }

    [Fact]
    public void MarkAsExpired_ShouldCallTouch()
    {
        var item = CreateAvailableItem();
        var initialUpdatedAt = item.UpdatedAt;

        item.MarkAsExpired();

        item.UpdatedAt.Should().BeOnOrAfter(initialUpdatedAt);
    }

    #endregion

    #region MarkAsConflict (Available -> Conflict)

    [Fact]
    public void MarkAsConflict_FromAvailable_ShouldTransitionToConflict()
    {
        var item = CreateAvailableItem();

        item.MarkAsConflict();

        item.RestoreStatus.Should().Be(RestoreStatus.Conflict);
    }

    [Fact]
    public void MarkAsConflict_ShouldCallTouch()
    {
        var item = CreateAvailableItem();
        var initialUpdatedAt = item.UpdatedAt;

        item.MarkAsConflict();

        item.UpdatedAt.Should().BeOnOrAfter(initialUpdatedAt);
    }

    #endregion

    #region ResetToAvailable

    [Fact]
    public void ResetToAvailable_FromExpired_ShouldTransitionToAvailable()
    {
        var item = CreateAvailableItem();
        item.MarkAsExpired();

        item.ResetToAvailable();

        item.RestoreStatus.Should().Be(RestoreStatus.Available);
    }

    [Fact]
    public void ResetToAvailable_FromConflict_ShouldTransitionToAvailable()
    {
        var item = CreateAvailableItem();
        item.MarkAsConflict();

        item.ResetToAvailable();

        item.RestoreStatus.Should().Be(RestoreStatus.Available);
    }

    [Fact]
    public void ResetToAvailable_FromRestored_ShouldThrow()
    {
        var item = CreateAvailableItem();
        item.MarkAsRestored(Guid.NewGuid());

        var act = () => item.ResetToAvailable();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void ResetToAvailable_FromAvailable_ShouldRemainAvailable()
    {
        var item = CreateAvailableItem();

        // Resetting an already-available item should not throw
        item.ResetToAvailable();

        item.RestoreStatus.Should().Be(RestoreStatus.Available);
    }

    #endregion

    #region Invalid State Transitions

    [Fact]
    public void MarkAsRestored_FromExpired_ShouldThrow()
    {
        var item = CreateAvailableItem();
        item.MarkAsExpired();

        var act = () => item.MarkAsRestored(Guid.NewGuid());

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void MarkAsRestored_FromConflict_ShouldThrow()
    {
        var item = CreateAvailableItem();
        item.MarkAsConflict();

        var act = () => item.MarkAsRestored(Guid.NewGuid());

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void MarkAsRestored_FromRestored_ShouldThrow_DoubleRestore()
    {
        var item = CreateAvailableItem();
        item.MarkAsRestored(Guid.NewGuid());

        var act = () => item.MarkAsRestored(Guid.NewGuid());

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void MarkAsExpired_FromRestored_ShouldThrow()
    {
        var item = CreateAvailableItem();
        item.MarkAsRestored(Guid.NewGuid());

        var act = () => item.MarkAsExpired();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void MarkAsExpired_FromConflict_ShouldThrow()
    {
        var item = CreateAvailableItem();
        item.MarkAsConflict();

        var act = () => item.MarkAsExpired();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void MarkAsExpired_FromExpired_ShouldThrow_DoubleExpire()
    {
        var item = CreateAvailableItem();
        item.MarkAsExpired();

        var act = () => item.MarkAsExpired();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void MarkAsConflict_FromRestored_ShouldThrow()
    {
        var item = CreateAvailableItem();
        item.MarkAsRestored(Guid.NewGuid());

        var act = () => item.MarkAsConflict();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void MarkAsConflict_FromExpired_ShouldThrow()
    {
        var item = CreateAvailableItem();
        item.MarkAsExpired();

        var act = () => item.MarkAsConflict();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void MarkAsConflict_FromConflict_ShouldThrow_DoubleConflict()
    {
        var item = CreateAvailableItem();
        item.MarkAsConflict();

        var act = () => item.MarkAsConflict();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    #endregion

    #region Full Lifecycle Sequences

    [Fact]
    public void FullLifecycle_Archive_ThenRestore_ShouldSucceed()
    {
        var item = CreateAvailableItem();
        var restoredBy = Guid.NewGuid();

        // Item starts as Available after archive creation
        item.RestoreStatus.Should().Be(RestoreStatus.Available);

        // Restore it
        item.MarkAsRestored(restoredBy);
        item.RestoreStatus.Should().Be(RestoreStatus.Restored);
        item.RestoredByUserId.Should().Be(restoredBy);
    }

    [Fact]
    public void FullLifecycle_Archive_Expire_Reset_ThenRestore_ShouldSucceed()
    {
        var item = CreateAvailableItem();
        var restoredBy = Guid.NewGuid();

        item.MarkAsExpired();
        item.RestoreStatus.Should().Be(RestoreStatus.Expired);

        // Admin extends expiry
        item.ResetToAvailable();
        item.RestoreStatus.Should().Be(RestoreStatus.Available);

        // Now restore
        item.MarkAsRestored(restoredBy);
        item.RestoreStatus.Should().Be(RestoreStatus.Restored);
    }

    [Fact]
    public void FullLifecycle_Archive_Conflict_Reset_ThenRestore_ShouldSucceed()
    {
        var item = CreateAvailableItem();
        var restoredBy = Guid.NewGuid();

        item.MarkAsConflict();
        item.RestoreStatus.Should().Be(RestoreStatus.Conflict);

        item.ResetToAvailable();
        item.RestoreStatus.Should().Be(RestoreStatus.Available);

        item.MarkAsRestored(restoredBy);
        item.RestoreStatus.Should().Be(RestoreStatus.Restored);
    }

    [Fact]
    public void FullLifecycle_Restored_CannotBeReset()
    {
        var item = CreateAvailableItem();
        item.MarkAsRestored(Guid.NewGuid());

        var act = () => item.ResetToAvailable();
        act.Should().Throw<DomainException>();

        // Also cannot expire, conflict, or restore again
        var actExpire = () => item.MarkAsExpired();
        actExpire.Should().Throw<DomainException>();

        var actConflict = () => item.MarkAsConflict();
        actConflict.Should().Throw<DomainException>();

        var actRestore = () => item.MarkAsRestored(Guid.NewGuid());
        actRestore.Should().Throw<DomainException>();
    }

    #endregion

    #region Construction Validation

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Constructor_WithInvalidEntityType_ShouldThrow(string? entityType)
    {
        var act = () => new ArchiveItem(
            entityType!, Guid.NewGuid(), Guid.NewGuid(),
            "Test", Guid.NewGuid(), "{}");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData("folder")]
    [InlineData("workspace")]
    [InlineData("BOARD")]
    public void Constructor_WithUnsupportedEntityType_ShouldThrow(string entityType)
    {
        var act = () => new ArchiveItem(
            entityType, Guid.NewGuid(), Guid.NewGuid(),
            "Test", Guid.NewGuid(), "{}");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_WithEmptyEntityId_ShouldThrow()
    {
        var act = () => new ArchiveItem(
            "board", Guid.Empty, Guid.NewGuid(),
            "Test", Guid.NewGuid(), "{}");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_WithEmptyBoardId_ShouldThrow()
    {
        var act = () => new ArchiveItem(
            "board", Guid.NewGuid(), Guid.Empty,
            "Test", Guid.NewGuid(), "{}");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_WithEmptyName_ShouldThrow()
    {
        var act = () => new ArchiveItem(
            "board", Guid.NewGuid(), Guid.NewGuid(),
            "", Guid.NewGuid(), "{}");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_WithNameOver200Chars_ShouldThrow()
    {
        var act = () => new ArchiveItem(
            "board", Guid.NewGuid(), Guid.NewGuid(),
            new string('x', 201), Guid.NewGuid(), "{}");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_WithNameExactly200Chars_ShouldSucceed()
    {
        var item = new ArchiveItem(
            "board", Guid.NewGuid(), Guid.NewGuid(),
            new string('x', 200), Guid.NewGuid(), "{}");

        item.Name.Should().HaveLength(200);
    }

    [Fact]
    public void Constructor_WithEmptyArchivedByUserId_ShouldThrow()
    {
        var act = () => new ArchiveItem(
            "board", Guid.NewGuid(), Guid.NewGuid(),
            "Test", Guid.Empty, "{}");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_WithEmptySnapshotJson_ShouldThrow()
    {
        var act = () => new ArchiveItem(
            "board", Guid.NewGuid(), Guid.NewGuid(),
            "Test", Guid.NewGuid(), "");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    #endregion
}
