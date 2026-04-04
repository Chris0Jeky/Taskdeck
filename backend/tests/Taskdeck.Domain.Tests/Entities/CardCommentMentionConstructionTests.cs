using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class CardCommentMentionConstructionTests
{
    private static readonly Guid ValidCommentId = Guid.NewGuid();
    private static readonly Guid ValidUserId = Guid.NewGuid();
    private const string ValidUsername = "johndoe";

    [Fact]
    public void Constructor_ValidArgs_SetsProperties()
    {
        var mention = new CardCommentMention(ValidCommentId, ValidUserId, ValidUsername);

        mention.CardCommentId.Should().Be(ValidCommentId);
        mention.MentionedUserId.Should().Be(ValidUserId);
        mention.MentionedUsername.Should().Be(ValidUsername);
        mention.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Constructor_EmptyCommentId_Throws()
    {
        var act = () => new CardCommentMention(Guid.Empty, ValidUserId, ValidUsername);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_EmptyUserId_Throws()
    {
        var act = () => new CardCommentMention(ValidCommentId, Guid.Empty, ValidUsername);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_EmptyUsername_Throws(string? username)
    {
        var act = () => new CardCommentMention(ValidCommentId, ValidUserId, username!);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_UsernameExactly50Chars_Succeeds()
    {
        var username = new string('a', 50);
        var mention = new CardCommentMention(ValidCommentId, ValidUserId, username);
        mention.MentionedUsername.Should().HaveLength(50);
    }

    [Fact]
    public void Constructor_UsernameOver50Chars_Throws()
    {
        var username = new string('a', 51);
        var act = () => new CardCommentMention(ValidCommentId, ValidUserId, username);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }
}
