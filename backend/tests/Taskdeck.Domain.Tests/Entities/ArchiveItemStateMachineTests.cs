using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ArchiveItemStateMachineTests
{
    private static readonly Guid ValidEntityId = Guid.NewGuid();
    private static readonly Guid ValidBoardId = Guid.NewGuid();
    private static readonly Guid ValidUserId = Guid.NewGuid();
    private const string ValidSnapshot = "{\"id\":\"abc\"}";

    private static ArchiveItem CreateAvailableItem(string entityType = "card", string name = "My Card") =>
        new(entityType, ValidEntityId, ValidBoardId, name, ValidUserId, ValidSnapshot);

    #region Constructor validation

    [Fact]
    public void Constructor_ValidArgs_CreatesAvailableItem()
    {
        var item = CreateAvailableItem();

        item.EntityType.Should().Be("card");
        item.EntityId.Should().Be(ValidEntityId);
        item.BoardId.Should().Be(ValidBoardId);
        item.Name.Should().Be("My Card");
        item.ArchivedByUserId.Should().Be(ValidUserId);
        item.SnapshotJson.Should().Be(ValidSnapshot);
        item.RestoreStatus.Should().Be(RestoreStatus.Available);
        item.Reason.Should().BeNull();
        item.RestoredAt.Should().BeNull();
        item.RestoredByUserId.Should().BeNull();
        item.ArchivedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Constructor_WithReason_SetsReason()
    {
        var item = new ArchiveItem("board", ValidEntityId, ValidBoardId, "Board1", ValidUserId, ValidSnapshot, "cleanup");
        item.Reason.Should().Be("cleanup");
    }

    [Theory]
    [InlineData("board")]
    [InlineData("column")]
    [InlineData("card")]
    public void Constructor_ValidEntityType_Succeeds(string entityType)
    {
        var item = CreateAvailableItem(entityType);
        item.EntityType.Should().Be(entityType);
    }

    [Theory]
    [InlineData("task")]
    [InlineData("Task")]
    [InlineData("BOARD")]
    [InlineData("label")]
    [InlineData("user")]
    public void Constructor_InvalidEntityType_Throws(string entityType)
    {
        var act = () => new ArchiveItem(entityType, ValidEntityId, ValidBoardId, "Name", ValidUserId, ValidSnapshot);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_EmptyEntityType_Throws(string? entityType)
    {
        var act = () => new ArchiveItem(entityType!, ValidEntityId, ValidBoardId, "Name", ValidUserId, ValidSnapshot);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_EmptyEntityId_Throws()
    {
        var act = () => new ArchiveItem("card", Guid.Empty, ValidBoardId, "Name", ValidUserId, ValidSnapshot);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_EmptyBoardId_Throws()
    {
        var act = () => new ArchiveItem("card", ValidEntityId, Guid.Empty, "Name", ValidUserId, ValidSnapshot);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_EmptyName_Throws(string? name)
    {
        var act = () => new ArchiveItem("card", ValidEntityId, ValidBoardId, name!, ValidUserId, ValidSnapshot);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_NameExactly200Chars_Succeeds()
    {
        var name = new string('a', 200);
        var item = CreateAvailableItem(name: name);
        item.Name.Should().HaveLength(200);
    }

    [Fact]
    public void Constructor_NameOver200Chars_Throws()
    {
        var name = new string('a', 201);
        var act = () => new ArchiveItem("card", ValidEntityId, ValidBoardId, name, ValidUserId, ValidSnapshot);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_EmptyArchivedByUserId_Throws()
    {
        var act = () => new ArchiveItem("card", ValidEntityId, ValidBoardId, "Name", Guid.Empty, ValidSnapshot);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_EmptySnapshotJson_Throws(string? snapshot)
    {
        var act = () => new ArchiveItem("card", ValidEntityId, ValidBoardId, "Name", ValidUserId, snapshot!);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    #endregion

    #region Valid transitions

    [Fact]
    public void Available_MarkAsRestored_TransitionsToRestored()
    {
        var item = CreateAvailableItem();
        var restorer = Guid.NewGuid();

        item.MarkAsRestored(restorer);

        item.RestoreStatus.Should().Be(RestoreStatus.Restored);
        item.RestoredByUserId.Should().Be(restorer);
        item.RestoredAt.Should().NotBeNull();
    }

    [Fact]
    public void Available_MarkAsExpired_TransitionsToExpired()
    {
        var item = CreateAvailableItem();

        item.MarkAsExpired();

        item.RestoreStatus.Should().Be(RestoreStatus.Expired);
    }

    [Fact]
    public void Available_MarkAsConflict_TransitionsToConflict()
    {
        var item = CreateAvailableItem();

        item.MarkAsConflict();

        item.RestoreStatus.Should().Be(RestoreStatus.Conflict);
    }

    [Fact]
    public void Expired_ResetToAvailable_TransitionsToAvailable()
    {
        var item = CreateAvailableItem();
        item.MarkAsExpired();

        item.ResetToAvailable();

        item.RestoreStatus.Should().Be(RestoreStatus.Available);
    }

    [Fact]
    public void Conflict_ResetToAvailable_TransitionsToAvailable()
    {
        var item = CreateAvailableItem();
        item.MarkAsConflict();

        item.ResetToAvailable();

        item.RestoreStatus.Should().Be(RestoreStatus.Available);
    }

    #endregion

    #region Invalid transitions

    [Fact]
    public void Restored_ResetToAvailable_Throws()
    {
        var item = CreateAvailableItem();
        item.MarkAsRestored(Guid.NewGuid());

        var act = () => item.ResetToAvailable();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void Restored_MarkAsExpired_Throws()
    {
        var item = CreateAvailableItem();
        item.MarkAsRestored(Guid.NewGuid());

        var act = () => item.MarkAsExpired();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void Restored_MarkAsConflict_Throws()
    {
        var item = CreateAvailableItem();
        item.MarkAsRestored(Guid.NewGuid());

        var act = () => item.MarkAsConflict();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void Restored_MarkAsRestored_Throws()
    {
        var item = CreateAvailableItem();
        item.MarkAsRestored(Guid.NewGuid());

        var act = () => item.MarkAsRestored(Guid.NewGuid());

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void Expired_MarkAsRestored_Throws()
    {
        var item = CreateAvailableItem();
        item.MarkAsExpired();

        var act = () => item.MarkAsRestored(Guid.NewGuid());

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void Expired_MarkAsExpired_Throws()
    {
        var item = CreateAvailableItem();
        item.MarkAsExpired();

        var act = () => item.MarkAsExpired();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void Expired_MarkAsConflict_Throws()
    {
        var item = CreateAvailableItem();
        item.MarkAsExpired();

        var act = () => item.MarkAsConflict();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void Conflict_MarkAsRestored_Throws()
    {
        var item = CreateAvailableItem();
        item.MarkAsConflict();

        var act = () => item.MarkAsRestored(Guid.NewGuid());

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void Conflict_MarkAsExpired_Throws()
    {
        var item = CreateAvailableItem();
        item.MarkAsConflict();

        var act = () => item.MarkAsExpired();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void Conflict_MarkAsConflict_Throws()
    {
        var item = CreateAvailableItem();
        item.MarkAsConflict();

        var act = () => item.MarkAsConflict();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void Available_ResetToAvailable_DoesNotThrow()
    {
        // Available -> ResetToAvailable: not Restored, so allowed
        var item = CreateAvailableItem();

        item.ResetToAvailable();

        item.RestoreStatus.Should().Be(RestoreStatus.Available);
    }

    #endregion

    #region MarkAsRestored validation

    [Fact]
    public void MarkAsRestored_EmptyUserId_Throws()
    {
        var item = CreateAvailableItem();

        var act = () => item.MarkAsRestored(Guid.Empty);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    #endregion

    #region Touch verification

    [Fact]
    public void MarkAsRestored_UpdatesTimestamp()
    {
        var item = CreateAvailableItem();
        var before = item.UpdatedAt;

        item.MarkAsRestored(Guid.NewGuid());

        item.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void MarkAsExpired_UpdatesTimestamp()
    {
        var item = CreateAvailableItem();
        var before = item.UpdatedAt;

        item.MarkAsExpired();

        item.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void MarkAsConflict_UpdatesTimestamp()
    {
        var item = CreateAvailableItem();
        var before = item.UpdatedAt;

        item.MarkAsConflict();

        item.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void ResetToAvailable_UpdatesTimestamp()
    {
        var item = CreateAvailableItem();
        item.MarkAsExpired();
        var before = item.UpdatedAt;

        item.ResetToAvailable();

        item.UpdatedAt.Should().BeOnOrAfter(before);
    }

    #endregion

    #region Round-trip transitions

    [Fact]
    public void Expired_ResetToAvailable_ThenRestore_Works()
    {
        var item = CreateAvailableItem();
        item.MarkAsExpired();
        item.ResetToAvailable();

        item.MarkAsRestored(Guid.NewGuid());

        item.RestoreStatus.Should().Be(RestoreStatus.Restored);
    }

    [Fact]
    public void Conflict_ResetToAvailable_ThenExpire_Works()
    {
        var item = CreateAvailableItem();
        item.MarkAsConflict();
        item.ResetToAvailable();

        item.MarkAsExpired();

        item.RestoreStatus.Should().Be(RestoreStatus.Expired);
    }

    #endregion
}
