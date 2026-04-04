using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ChatSessionStateMachineTests
{
    private static readonly Guid ValidUserId = Guid.NewGuid();
    private static readonly Guid ValidBoardId = Guid.NewGuid();
    private const string ValidTitle = "Chat about tasks";

    private static ChatSession CreateActiveSession(Guid? boardId = null) =>
        new(ValidUserId, ValidTitle, boardId);

    private static ChatSession CreateArchivedSession()
    {
        var session = CreateActiveSession();
        session.Archive();
        return session;
    }

    private static ChatMessage CreateMessage(Guid sessionId) =>
        new(sessionId, ChatMessageRole.User, "Hello", "text");

    #region Constructor validation

    [Fact]
    public void Constructor_ValidArgs_CreatesActiveSession()
    {
        var session = CreateActiveSession();

        session.UserId.Should().Be(ValidUserId);
        session.Title.Should().Be(ValidTitle);
        session.Status.Should().Be(ChatSessionStatus.Active);
        session.BoardId.Should().BeNull();
        session.Messages.Should().BeEmpty();
        session.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Constructor_WithBoardId_SetsBoardId()
    {
        var session = CreateActiveSession(ValidBoardId);
        session.BoardId.Should().Be(ValidBoardId);
    }

    [Fact]
    public void Constructor_EmptyUserId_Throws()
    {
        var act = () => new ChatSession(Guid.Empty, ValidTitle);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_EmptyTitle_Throws(string? title)
    {
        var act = () => new ChatSession(ValidUserId, title!);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_TitleExactly200Chars_Succeeds()
    {
        var title = new string('t', 200);
        var session = new ChatSession(ValidUserId, title);
        session.Title.Should().HaveLength(200);
    }

    [Fact]
    public void Constructor_TitleOver200Chars_Throws()
    {
        var title = new string('t', 201);
        var act = () => new ChatSession(ValidUserId, title);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    #endregion

    #region UpdateTitle

    [Fact]
    public void UpdateTitle_ValidTitle_UpdatesTitle()
    {
        var session = CreateActiveSession();

        session.UpdateTitle("New Title");

        session.Title.Should().Be("New Title");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void UpdateTitle_EmptyTitle_Throws(string? title)
    {
        var session = CreateActiveSession();

        var act = () => session.UpdateTitle(title!);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void UpdateTitle_Over200Chars_Throws()
    {
        var session = CreateActiveSession();
        var title = new string('t', 201);

        var act = () => session.UpdateTitle(title);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void UpdateTitle_UpdatesTimestamp()
    {
        var session = CreateActiveSession();
        var before = session.UpdatedAt;

        session.UpdateTitle("Changed");

        session.UpdatedAt.Should().BeOnOrAfter(before);
    }

    #endregion

    #region Archive / Reactivate state machine

    [Fact]
    public void Active_Archive_TransitionsToArchived()
    {
        var session = CreateActiveSession();

        session.Archive();

        session.Status.Should().Be(ChatSessionStatus.Archived);
    }

    [Fact]
    public void Archived_Archive_Throws()
    {
        var session = CreateArchivedSession();

        var act = () => session.Archive();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void Archived_Reactivate_TransitionsToActive()
    {
        var session = CreateArchivedSession();

        session.Reactivate();

        session.Status.Should().Be(ChatSessionStatus.Active);
    }

    [Fact]
    public void Active_Reactivate_Throws()
    {
        var session = CreateActiveSession();

        var act = () => session.Reactivate();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void Archive_UpdatesTimestamp()
    {
        var session = CreateActiveSession();
        var before = session.UpdatedAt;

        session.Archive();

        session.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Reactivate_UpdatesTimestamp()
    {
        var session = CreateArchivedSession();
        var before = session.UpdatedAt;

        session.Reactivate();

        session.UpdatedAt.Should().BeOnOrAfter(before);
    }

    #endregion

    #region AddMessage

    [Fact]
    public void Active_AddMessage_AddsToCollection()
    {
        var session = CreateActiveSession();
        var msg = CreateMessage(session.Id);

        session.AddMessage(msg);

        session.Messages.Should().HaveCount(1);
        session.Messages[0].Should().BeSameAs(msg);
    }

    [Fact]
    public void Active_AddMultipleMessages_PreservesOrder()
    {
        var session = CreateActiveSession();
        var msg1 = new ChatMessage(session.Id, ChatMessageRole.User, "First", "text");
        var msg2 = new ChatMessage(session.Id, ChatMessageRole.Assistant, "Second", "text");

        session.AddMessage(msg1);
        session.AddMessage(msg2);

        session.Messages.Should().HaveCount(2);
        session.Messages[0].Content.Should().Be("First");
        session.Messages[1].Content.Should().Be("Second");
    }

    [Fact]
    public void Archived_AddMessage_Throws()
    {
        var session = CreateArchivedSession();
        var msg = CreateMessage(session.Id);

        var act = () => session.AddMessage(msg);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void AddMessage_UpdatesTimestamp()
    {
        var session = CreateActiveSession();
        var before = session.UpdatedAt;
        var msg = CreateMessage(session.Id);

        session.AddMessage(msg);

        session.UpdatedAt.Should().BeOnOrAfter(before);
    }

    #endregion

    #region Round-trip transitions

    [Fact]
    public void Archive_ThenReactivate_ThenAddMessage_Works()
    {
        var session = CreateActiveSession();
        session.Archive();
        session.Reactivate();

        var msg = CreateMessage(session.Id);
        session.AddMessage(msg);

        session.Status.Should().Be(ChatSessionStatus.Active);
        session.Messages.Should().HaveCount(1);
    }

    [Fact]
    public void Archive_ThenReactivate_ThenArchiveAgain_Works()
    {
        var session = CreateActiveSession();
        session.Archive();
        session.Reactivate();

        session.Archive();

        session.Status.Should().Be(ChatSessionStatus.Archived);
    }

    [Fact]
    public void UpdateTitle_WorksOnArchivedSession()
    {
        // UpdateTitle does not check status — verify it works in both states
        var session = CreateArchivedSession();

        session.UpdateTitle("Archived title update");

        session.Title.Should().Be("Archived title update");
    }

    #endregion
}
