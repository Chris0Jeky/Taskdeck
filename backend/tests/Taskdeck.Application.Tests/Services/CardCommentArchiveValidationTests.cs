using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class CardCommentArchiveValidationTests
{
    private readonly Mock<IUnitOfWork> _unit = new();
    private readonly Mock<IBoardRepository> _boards = new();
    private readonly Mock<ICardRepository> _cards = new();
    private readonly Mock<ICardCommentRepository> _comments = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IAuditLogRepository> _audits = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly User _actor = new("author", "author@example.test", "hash");
    private readonly Board _board;
    private readonly Card _card;
    private readonly CardComment _comment;
    private readonly CardCommentService _service;

    public CardCommentArchiveValidationTests()
    {
        _board = new Board("Validation", ownerId: _actor.Id);
        _card = new Card(_board.Id, Guid.NewGuid(), "Card");
        _comment = new CardComment(_card.Id, _board.Id, _actor.Id, "Before");
        _comment.ReplaceMentions(new[] { (Guid.NewGuid(), "reader") });
        _unit.SetupGet(u => u.Boards).Returns(_boards.Object);
        _unit.SetupGet(u => u.Cards).Returns(_cards.Object);
        _unit.SetupGet(u => u.CardComments).Returns(_comments.Object);
        _unit.SetupGet(u => u.Users).Returns(_users.Object);
        _unit.SetupGet(u => u.AuditLogs).Returns(_audits.Object);
        _unit.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _boards.Setup(r => r.GetByIdAsync(_board.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_board);
        _cards.Setup(r => r.GetByIdAsync(_card.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_card);
        _comments.Setup(r => r.GetByIdWithMentionsAsync(_comment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_comment);
        _comments.Setup(r => r.AddAsync(It.IsAny<CardComment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CardComment comment, CancellationToken _) => comment);
        _audits.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuditLog audit, CancellationToken _) => audit);
        _users.Setup(r => r.GetByIdAsync(_actor.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_actor);
        _notifications.Setup(s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));
        _service = new CardCommentService(_unit.Object, _notifications.Object);
    }

    [Theory]
    [InlineData("create", false)]
    [InlineData("create", true)]
    [InlineData("update", false)]
    [InlineData("update", true)]
    public async Task OverlongContent_KeepsValidationErrorBeforeArchiveRefusal(string operation, bool archived)
    {
        if (archived) _board.Archive();
        var before = Snapshot();

        var result = await MutateAsync(operation, new string('x', 4001));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Be("Comment content cannot exceed 4000 characters");
        Snapshot().Should().BeEquivalentTo(before);
        AssertNoWrites();
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(true, 10)]
    [InlineData(false, 4001)]
    [InlineData(true, 4001)]
    public async Task DeletedComment_KeepsDeletedErrorBeforeLengthAndArchiveRefusal(bool archived, int length)
    {
        _comment.SoftDelete();
        if (archived) _board.Archive();
        var before = Snapshot();

        var result = await MutateAsync("update", new string('x', length));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Be("Deleted comments cannot be edited");
        Snapshot().Should().BeEquivalentTo(before);
        AssertNoWrites();
    }

    [Theory]
    [InlineData("create", false)]
    [InlineData("create", true)]
    [InlineData("update", false)]
    [InlineData("update", true)]
    public async Task MaximumLength_StillUsesArchiveBoundaryWithoutPrematureMutation(string operation, bool archived)
    {
        if (archived) _board.Archive();
        var before = Snapshot();

        var result = await MutateAsync(operation, new string('x', 4000));

        if (archived)
        {
            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
            result.ErrorMessage.Should().Contain("Restore the board");
            Snapshot().Should().BeEquivalentTo(before);
            AssertNoWrites();
        }
        else
        {
            result.IsSuccess.Should().BeTrue();
            result.Value.Content.Should().HaveLength(4000);
            _unit.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    private object Snapshot() => new
    {
        _comment.Content,
        _comment.UpdatedAt,
        _comment.EditedAt,
        _comment.DeletedAt,
        _comment.IsDeleted,
        MentionIds = _comment.Mentions.Select(mention => mention.Id).ToArray()
    };

    private Task<Result<CardCommentDto>> MutateAsync(string operation, string content) => operation switch
    {
        "create" => _service.CreateCommentAsync(_board.Id, _card.Id, _actor.Id, new CreateCardCommentDto(content)),
        "update" => _service.UpdateCommentAsync(_board.Id, _card.Id, _comment.Id, _actor.Id, new UpdateCardCommentDto(content)),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private void AssertNoWrites()
    {
        _comments.Verify(r => r.AddAsync(It.IsAny<CardComment>(), It.IsAny<CancellationToken>()), Times.Never);
        _audits.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifications.Verify(s => s.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
        _unit.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
