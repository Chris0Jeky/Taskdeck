using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class CardCommentArchiveTests
{
    private readonly Mock<IUnitOfWork> _unit = new();
    private readonly Mock<IBoardRepository> _boards = new();
    private readonly Mock<ICardRepository> _cards = new();
    private readonly Mock<ICardCommentRepository> _comments = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IAuditLogRepository> _audits = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IAuthorizationService> _authorization = new();
    private readonly User _actor = new("author", "author@example.test", "hash");
    private readonly User _reader = new("reader", "reader@example.test", "hash");
    private readonly Board _board;
    private readonly Card _card;
    private readonly CardComment _comment;
    private readonly CardCommentService _service;

    public CardCommentArchiveTests()
    {
        _board = new Board("Comments", ownerId: _actor.Id);
        _card = new Card(_board.Id, Guid.NewGuid(), "Card");
        _comment = new CardComment(_card.Id, _board.Id, _actor.Id, "Before @reader");
        _comment.ReplaceMentions(new[] { (_reader.Id, _reader.Username) });
        _unit.SetupGet(u => u.Boards).Returns(_boards.Object);
        _unit.SetupGet(u => u.Cards).Returns(_cards.Object);
        _unit.SetupGet(u => u.CardComments).Returns(_comments.Object);
        _unit.SetupGet(u => u.Users).Returns(_users.Object);
        _unit.SetupGet(u => u.AuditLogs).Returns(_audits.Object);
        _unit.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _boards.Setup(r => r.GetByIdAsync(_board.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_board);
        _cards.Setup(r => r.GetByIdAsync(_card.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_card);
        _comments.Setup(r => r.GetByIdWithMentionsAsync(_comment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_comment);
        _comments.Setup(r => r.GetByCardIdAsync(_card.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { _comment });
        _comments.Setup(r => r.AddAsync(It.IsAny<CardComment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CardComment comment, CancellationToken _) => comment);
        _audits.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuditLog log, CancellationToken _) => log);
        _users.Setup(r => r.GetByIdAsync(_actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_actor);
        _users.Setup(r => r.GetByUsernameAsync(_reader.Username, It.IsAny<CancellationToken>())).ReturnsAsync(_reader);
        _authorization.Setup(s => s.CanReadBoardAsync(_reader.Id, _board.Id)).ReturnsAsync(Result.Success(true));
        _notifications.Setup(s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));
        _service = new CardCommentService(_unit.Object, _notifications.Object, _authorization.Object);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task ArchivedBoard_RefusesCommentMutationWithoutSideEffects(string operation)
    {
        _board.Archive();
        var stamp = _comment.UpdatedAt;
        var mentions = _comment.Mentions.Select(m => m.Id).ToArray();

        var result = await MutateAsync(operation);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        result.ErrorMessage.Should().Contain("Restore the board");
        _comment.Content.Should().Be("Before @reader");
        _comment.UpdatedAt.Should().Be(stamp);
        _comment.IsDeleted.Should().BeFalse();
        _comment.EditedAt.Should().BeNull();
        _comment.DeletedAt.Should().BeNull();
        _comment.Mentions.Select(m => m.Id).Should().Equal(mentions);
        AssertNoWrites();
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task MissingBoard_RefusesCommentMutation(string operation)
    {
        _boards.Setup(r => r.GetByIdAsync(_board.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Board?)null);

        var result = await MutateAsync(operation);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        AssertNoWrites();
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task RestoredBoard_AllowsTheSameCommentMutation(string operation)
    {
        _board.Archive();
        _board.Unarchive();

        var result = await MutateAsync(operation);

        result.IsSuccess.Should().BeTrue();
        _unit.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task ArchivedCard_OnActiveBoardRetainsDiscussionBehavior(string operation)
    {
        _card.Archive();

        var result = await MutateAsync(operation);

        result.IsSuccess.Should().BeTrue();
        _card.IsArchived.Should().BeTrue();
        _unit.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task MismatchedBoard_IsRejectedBeforeArchiveLookup(string operation)
    {
        _board.Archive();

        var result = await MutateAsync(operation, boardId: Guid.NewGuid());

        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        _boards.VerifyNoOtherCalls();
        AssertNoWrites();
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task NonAuthorEditor_IsRejectedBeforeArchiveLookup(string operation)
    {
        _board.Archive();
        var otherActor = Guid.NewGuid();
        _authorization.Setup(s => s.GetUserRoleForBoardAsync(otherActor, _board.Id))
            .ReturnsAsync(Result.Success<UserRole?>(UserRole.Editor));

        var result = await MutateAsync(operation, actorId: otherActor);

        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _boards.VerifyNoOtherCalls();
        AssertNoWrites();
    }

    [Fact]
    public async Task ArchivedBoard_StillAllowsReadingItsComments()
    {
        _board.Archive();

        var result = await _service.GetCommentsAsync(_board.Id, _card.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Content.Should().Be("Before @reader");
        _boards.VerifyNoOtherCalls();
        AssertNoWrites();
    }

    private async Task<Result> MutateAsync(string operation, Guid? boardId = null, Guid? actorId = null)
    {
        var targetBoard = boardId ?? _board.Id;
        var actor = actorId ?? _actor.Id;
        return operation switch
        {
            "create" => await _service.CreateCommentAsync(targetBoard, _card.Id, actor, new CreateCardCommentDto("After @reader")),
            "update" => await _service.UpdateCommentAsync(targetBoard, _card.Id, _comment.Id, actor, new UpdateCardCommentDto("After @reader")),
            "delete" => await _service.DeleteCommentAsync(targetBoard, _card.Id, _comment.Id, actor),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private void AssertNoWrites()
    {
        _comments.Verify(r => r.AddAsync(It.IsAny<CardComment>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(r => r.GetByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _audits.VerifyNoOtherCalls();
        _notifications.VerifyNoOtherCalls();
        _unit.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
