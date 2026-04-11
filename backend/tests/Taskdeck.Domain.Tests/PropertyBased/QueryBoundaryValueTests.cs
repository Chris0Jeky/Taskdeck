using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.PropertyBased;

/// <summary>
/// Boundary-value tests for inputs that exercise SQLite query edge cases.
/// Covers GUID format variations, DateTime boundaries, and string
/// edge cases (empty vs null vs whitespace vs max-length).
/// Key property: domain entities handle all these values correctly
/// without silently corrupting data or throwing unhandled exceptions.
/// </summary>
public class QueryBoundaryValueTests
{
    private const int MaxTests = 200;

    // ─────────────────────── GUID format variations ───────────────────────

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]  // Guid.Empty
    [InlineData("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF")]  // uppercase max
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]  // lowercase max
    [InlineData("aAbBcCdD-eEfF-1234-5678-9aBcDeFfAaBb")]  // mixed case
    [InlineData("12345678-1234-1234-1234-123456789abc")]  // typical
    public void GuidFormat_RoundTrips_ThroughToString(string guidStr)
    {
        var guid = Guid.Parse(guidStr);
        // .NET normalizes GUID to lowercase with hyphens
        var roundTripped = Guid.Parse(guid.ToString());
        roundTripped.Should().Be(guid);
    }

    [Fact]
    public void GuidEmpty_RejectedForEntityId()
    {
        var act = () => new Card(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "Title");
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void GuidEmpty_RejectedForBoardOwnerId()
    {
        var act = () => new Board("Test", ownerId: Guid.Empty);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Property(MaxTest = MaxTests)]
    public Property RandomGuid_AlwaysAccepted_AsCardBoardId()
    {
        return Prop.ForAll(
            Arb.From(Gen.Fresh(() => Guid.NewGuid())),
            guid =>
            {
                var card = new Card(guid, Guid.NewGuid(), "Title");
                card.BoardId.Should().Be(guid);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property RandomGuid_AlwaysAccepted_AsColumnBoardId()
    {
        return Prop.ForAll(
            Arb.From(Gen.Fresh(() => Guid.NewGuid())),
            guid =>
            {
                var column = new Column(guid, "Col", 0);
                column.BoardId.Should().Be(guid);
            });
    }

    // ─────────────────────── DateTime boundary values ───────────────────────

    [Fact]
    public void Board_CreatedAt_IsRecentUtc()
    {
        var board = new Board("Test");
        board.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Card_CreatedAt_IsRecentUtc()
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Title");
        card.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Column_CreatedAt_IsRecentUtc()
    {
        var column = new Column(Guid.NewGuid(), "Col", 0);
        column.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("0001-01-01T00:00:00+00:00")]      // DateTimeOffset.MinValue
    [InlineData("9999-12-31T23:59:59.9999999+00:00")] // DateTimeOffset.MaxValue
    [InlineData("1970-01-01T00:00:00+00:00")]        // Unix epoch
    [InlineData("2038-01-19T03:14:07+00:00")]        // Y2038 problem boundary
    [InlineData("1999-12-31T23:59:59+00:00")]        // Y2K edge
    [InlineData("2000-01-01T00:00:00+00:00")]        // Y2K
    public void DateTimeOffset_Parse_AcceptsBoundaryValues(string dateStr)
    {
        var dto = DateTimeOffset.Parse(dateStr);
        // Round-trip through ISO 8601 string
        var roundTripped = DateTimeOffset.Parse(dto.ToString("O"));
        roundTripped.Should().Be(dto);
    }

    [Fact]
    public void Card_DueDate_AcceptsDistantFuture()
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Title");
        var farFuture = new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero);
        card.Update(dueDate: farFuture);
        card.DueDate.Should().Be(farFuture);
    }

    [Fact]
    public void Card_DueDate_AcceptsDistantPast()
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Title");
        var farPast = new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero);
        card.Update(dueDate: farPast);
        card.DueDate.Should().Be(farPast);
    }

    [Fact]
    public void Card_DueDate_AcceptsNull()
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Title");
        card.Update(dueDate: null);
        card.DueDate.Should().BeNull();
    }

    // ─────────────────────── String: empty vs null vs whitespace vs max-length ───────────────────────

    [Theory]
    [InlineData("", false)]        // empty - invalid
    [InlineData(" ", false)]       // single space - invalid
    [InlineData("  ", false)]      // multiple spaces - invalid
    [InlineData("\t", false)]      // tab - invalid
    [InlineData("\n", false)]      // newline - invalid
    [InlineData(" \t\n ", false)]  // mixed whitespace - invalid
    [InlineData("a", true)]        // single char - valid
    [InlineData(" a ", true)]      // leading/trailing whitespace with content - valid (not trimmed by domain)
    public void Board_Name_EmptyVsWhitespace_Boundaries(string name, bool shouldSucceed)
    {
        if (shouldSucceed)
        {
            var board = new Board(name);
            board.Name.Should().NotBeNullOrEmpty();
        }
        else
        {
            var act = () => new Board(name);
            act.Should().Throw<DomainException>()
                .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
        }
    }

    [Fact]
    public void Board_Name_AtMaxLength_Accepted()
    {
        var name = new string('x', 100);
        var board = new Board(name);
        board.Name.Length.Should().Be(100);
    }

    [Fact]
    public void Board_Name_OverMaxLength_Rejected()
    {
        var name = new string('x', 101);
        var act = () => new Board(name);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Card_Title_AtMaxLength_Accepted()
    {
        var title = new string('x', 200);
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), title);
        card.Title.Length.Should().Be(200);
    }

    [Fact]
    public void Card_Title_OverMaxLength_Rejected()
    {
        var title = new string('x', 201);
        var act = () => new Card(Guid.NewGuid(), Guid.NewGuid(), title);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Card_Description_AtMaxLength_Accepted()
    {
        var desc = new string('d', 2000);
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Title");
        card.Update(description: desc);
        card.Description.Length.Should().Be(2000);
    }

    [Fact]
    public void Card_Description_OverMaxLength_Rejected()
    {
        var desc = new string('d', 2001);
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Title");
        var act = () => card.Update(description: desc);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Card_Description_EmptyString_Accepted()
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Title");
        card.Update(description: "");
        card.Description.Should().Be("");
    }

    [Fact]
    public void Column_Name_AtMaxLength_Accepted()
    {
        var name = new string('c', 50);
        var col = new Column(Guid.NewGuid(), name, 0);
        col.Name.Length.Should().Be(50);
    }

    [Fact]
    public void Column_Name_OverMaxLength_Rejected()
    {
        var name = new string('c', 51);
        var act = () => new Column(Guid.NewGuid(), name, 0);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Label_Name_AtMaxLength_Accepted()
    {
        var name = new string('l', 30);
        var label = new Label(Guid.NewGuid(), name, "#FF0000");
        label.Name.Length.Should().Be(30);
    }

    [Fact]
    public void Label_Name_OverMaxLength_Rejected()
    {
        var name = new string('l', 31);
        var act = () => new Label(Guid.NewGuid(), name, "#FF0000");
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    // ─────────────────────── Position boundary values ───────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void Card_Position_NonNegative_Accepted(int position)
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Title");
        card.SetPosition(position);
        card.Position.Should().Be(position);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void Card_Position_Negative_Rejected(int position)
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Title");
        var act = () => card.SetPosition(position);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void Column_Position_NonNegative_Accepted(int position)
    {
        var col = new Column(Guid.NewGuid(), "Col", 0);
        col.SetPosition(position);
        col.Position.Should().Be(position);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Column_Position_Negative_Rejected(int position)
    {
        var col = new Column(Guid.NewGuid(), "Col", 0);
        var act = () => col.SetPosition(position);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    // ─────────────────────── WipLimit boundary values ───────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void Column_WipLimit_Positive_Accepted(int wipLimit)
    {
        var col = new Column(Guid.NewGuid(), "Col", 0, wipLimit);
        col.WipLimit.Should().Be(wipLimit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Column_WipLimit_ZeroOrNegative_Rejected(int wipLimit)
    {
        var act = () => new Column(Guid.NewGuid(), "Col", 0, wipLimit);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Column_WipLimit_Null_Accepted()
    {
        var col = new Column(Guid.NewGuid(), "Col", 0, null);
        col.WipLimit.Should().BeNull();
    }

    // ─────────────────────── Entity Touch property ───────────────────────

    // Verifies that entity mutations always set UpdatedAt.
    // Uses individual [Fact] tests instead of a property-based test to avoid
    // Thread.Sleep(1) * 200 iterations performance cost and timestamp resolution issues.

    [Fact]
    public void Board_Update_SetsUpdatedAt()
    {
        var board = new Board("Test");
        var initialUpdatedAt = board.UpdatedAt;
        // Ensure clock advances past timer resolution
        Thread.Sleep(16);
        board.Update(name: "Updated");
        board.UpdatedAt.Should().BeAfter(initialUpdatedAt);
    }

    [Fact]
    public void Card_Update_SetsUpdatedAt()
    {
        var card = new Card(Guid.NewGuid(), Guid.NewGuid(), "Title");
        var initialUpdatedAt = card.UpdatedAt;
        Thread.Sleep(16);
        card.Update(description: "Updated");
        card.UpdatedAt.Should().BeAfter(initialUpdatedAt);
    }

    [Fact]
    public void Column_SetPosition_SetsUpdatedAt()
    {
        var col = new Column(Guid.NewGuid(), "Col", 0);
        var initialUpdatedAt = col.UpdatedAt;
        Thread.Sleep(16);
        col.SetPosition(1);
        col.UpdatedAt.Should().BeAfter(initialUpdatedAt);
    }

    [Fact]
    public void Label_Update_SetsUpdatedAt()
    {
        var label = new Label(Guid.NewGuid(), "Label", "#FF0000");
        var initialUpdatedAt = label.UpdatedAt;
        Thread.Sleep(16);
        label.Update(name: "Updated");
        label.UpdatedAt.Should().BeAfter(initialUpdatedAt);
    }
}
