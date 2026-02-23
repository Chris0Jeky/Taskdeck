using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class CardCommentServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ICardRepository> _cardRepositoryMock = new();
    private readonly Mock<ICardCommentRepository> _cardCommentRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IAuditLogRepository> _auditLogRepositoryMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<IAuthorizationService> _authorizationServiceMock = new();

    private readonly CardCommentService _service;

    public CardCommentServiceTests()
    {
        _unitOfWorkMock.SetupGet(unitOfWork => unitOfWork.Cards).Returns(_cardRepositoryMock.Object);
        _unitOfWorkMock.SetupGet(unitOfWork => unitOfWork.CardComments).Returns(_cardCommentRepositoryMock.Object);
        _unitOfWorkMock.SetupGet(unitOfWork => unitOfWork.Users).Returns(_userRepositoryMock.Object);
        _unitOfWorkMock.SetupGet(unitOfWork => unitOfWork.AuditLogs).Returns(_auditLogRepositoryMock.Object);
        _unitOfWorkMock.Setup(unitOfWork => unitOfWork.SaveChangesAsync(default)).ReturnsAsync(1);

        _cardCommentRepositoryMock
            .Setup(repository => repository.AddAsync(It.IsAny<CardComment>(), default))
            .ReturnsAsync((CardComment comment, CancellationToken _) => comment);
        _auditLogRepositoryMock
            .Setup(repository => repository.AddAsync(It.IsAny<AuditLog>(), default))
            .ReturnsAsync((AuditLog log, CancellationToken _) => log);
        _notificationServiceMock
            .Setup(service => service.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), default))
            .ReturnsAsync(Result.Success(true));

        _service = new CardCommentService(
            _unitOfWorkMock.Object,
            _notificationServiceMock.Object,
            _authorizationServiceMock.Object);
    }

    [Fact]
    public async Task CreateCommentAsync_ShouldPublishMentionNotification_ForReadableMention()
    {
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Mention card");
        var actor = new User("actor_user", "actor_user@example.com", "hash");
        var mentioned = new User("target_user", "target_user@example.com", "hash");

        _cardRepositoryMock
            .Setup(repository => repository.GetByIdAsync(card.Id, default))
            .ReturnsAsync(card);
        _userRepositoryMock
            .Setup(repository => repository.GetByIdAsync(actor.Id, default))
            .ReturnsAsync(actor);
        _userRepositoryMock
            .Setup(repository => repository.GetByUsernameAsync("target_user", default))
            .ReturnsAsync(mentioned);
        _authorizationServiceMock
            .Setup(service => service.CanReadBoardAsync(mentioned.Id, board.Id))
            .ReturnsAsync(Result.Success(true));

        var result = await _service.CreateCommentAsync(
            board.Id,
            card.Id,
            actor.Id,
            new CreateCardCommentDto("Please review this @target_user"),
            default);

        result.IsSuccess.Should().BeTrue();
        _notificationServiceMock.Verify(
            service => service.PublishAsync(
                It.Is<CreateNotificationRequestDto>(dto =>
                    dto.UserId == mentioned.Id &&
                    dto.Type == NotificationType.Mention &&
                    dto.SourceEntityType == "card-comment"),
                default),
            Times.Once);
    }

    [Fact]
    public async Task CreateCommentAsync_ShouldSkipMentionNotification_WhenMentionedUserCannotReadBoard()
    {
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Mention card");
        var actor = new User("actor_user", "actor_user@example.com", "hash");
        var mentioned = new User("target_user", "target_user@example.com", "hash");

        _cardRepositoryMock
            .Setup(repository => repository.GetByIdAsync(card.Id, default))
            .ReturnsAsync(card);
        _userRepositoryMock
            .Setup(repository => repository.GetByIdAsync(actor.Id, default))
            .ReturnsAsync(actor);
        _userRepositoryMock
            .Setup(repository => repository.GetByUsernameAsync("target_user", default))
            .ReturnsAsync(mentioned);
        _authorizationServiceMock
            .Setup(service => service.CanReadBoardAsync(mentioned.Id, board.Id))
            .ReturnsAsync(Result.Success(false));

        var result = await _service.CreateCommentAsync(
            board.Id,
            card.Id,
            actor.Id,
            new CreateCardCommentDto("Please review this @target_user"),
            default);

        result.IsSuccess.Should().BeTrue();
        _notificationServiceMock.Verify(
            service => service.PublishAsync(It.IsAny<CreateNotificationRequestDto>(), default),
            Times.Never);
    }

    [Fact]
    public async Task UpdateCommentAsync_ShouldReturnForbidden_WhenActorIsNotAuthorOrModerator()
    {
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Comment card");
        var author = new User("author_user", "author_user@example.com", "hash");
        var outsider = new User("outsider_user", "outsider_user@example.com", "hash");
        var comment = new CardComment(card.Id, board.Id, author.Id, "Initial comment");

        _cardCommentRepositoryMock
            .Setup(repository => repository.GetByIdWithMentionsAsync(comment.Id, default))
            .ReturnsAsync(comment);
        _cardRepositoryMock
            .Setup(repository => repository.GetByIdAsync(card.Id, default))
            .ReturnsAsync(card);
        _authorizationServiceMock
            .Setup(service => service.GetUserRoleForBoardAsync(outsider.Id, board.Id))
            .ReturnsAsync(Result.Success<UserRole?>(UserRole.Viewer));

        var result = await _service.UpdateCommentAsync(
            board.Id,
            card.Id,
            comment.Id,
            outsider.Id,
            new UpdateCardCommentDto("Updated by outsider"),
            default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task DeleteCommentAsync_ShouldAllowAdminModeration()
    {
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Comment card");
        var author = new User("author_user", "author_user@example.com", "hash");
        var admin = new User("admin_user", "admin_user@example.com", "hash");
        var comment = new CardComment(card.Id, board.Id, author.Id, "Moderation target");

        _cardCommentRepositoryMock
            .Setup(repository => repository.GetByIdWithMentionsAsync(comment.Id, default))
            .ReturnsAsync(comment);
        _authorizationServiceMock
            .Setup(service => service.GetUserRoleForBoardAsync(admin.Id, board.Id))
            .ReturnsAsync(Result.Success<UserRole?>(UserRole.Admin));

        var result = await _service.DeleteCommentAsync(
            board.Id,
            card.Id,
            comment.Id,
            admin.Id,
            default);

        result.IsSuccess.Should().BeTrue();
        comment.IsDeleted.Should().BeTrue();
        _unitOfWorkMock.Verify(unitOfWork => unitOfWork.SaveChangesAsync(default), Times.Once);
    }
}
