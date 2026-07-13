using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services.Pipeline;

public class ProposalOperationContractValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ShouldCacheBoundedEntityLookupsAcrossOperations()
    {
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "Now", 0);
        var card = new Card(boardId, column.Id, "Review proposal");
        var label = new Label(boardId, "urgent", "#FF0000");
        var unitOfWork = new Mock<IUnitOfWork>();
        var cards = new Mock<ICardRepository>();
        var columns = new Mock<IColumnRepository>();
        var labels = new Mock<ILabelRepository>();
        unitOfWork.Setup(instance => instance.Cards).Returns(cards.Object);
        unitOfWork.Setup(instance => instance.Columns).Returns(columns.Object);
        unitOfWork.Setup(instance => instance.Labels).Returns(labels.Object);
        cards.Setup(repository => repository.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        columns.Setup(repository => repository.GetByIdAsync(column.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);
        labels.Setup(repository => repository.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { label });
        var operations = new[]
        {
            CreateOperation(
                0,
                "create",
                null,
                new { boardId, columnId = column.Id, title = "New card", labelIds = new[] { label.Id } }),
            CreateOperation(
                1,
                "update",
                card.Id,
                new { cardId = card.Id, labelIds = new[] { label.Id } }),
            CreateOperation(
                2,
                "add_label",
                card.Id,
                new { cardId = card.Id, labelId = label.Id }),
            CreateOperation(
                3,
                "remove-label",
                card.Id,
                new { cardId = card.Id, labelName = label.Name })
        };

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, operations);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        cards.Verify(
            repository => repository.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()),
            Times.Once);
        columns.Verify(
            repository => repository.GetByIdAsync(column.Id, It.IsAny<CancellationToken>()),
            Times.Once);
        labels.Verify(
            repository => repository.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()),
            Times.Once);
        cards.Verify(
            repository => repository.GetByBoardIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        columns.Verify(
            repository => repository.GetByBoardIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("add--label")]
    [InlineData("add_-label")]
    [InlineData("remove__label")]
    [InlineData("add..label")]
    public async Task ValidateAsync_ShouldRejectLabelLikeAliasesNotRegisteredByApply(string actionType)
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var operation = CreateOperation(
            0,
            actionType,
            Guid.NewGuid(),
            new { cardId = Guid.NewGuid(), labelId = Guid.NewGuid() });

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, Guid.NewGuid(), new[] { operation });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Unsupported card label action alias");
        unitOfWork.VerifyNoOtherCalls();
    }

    private static ProposalOperationDto CreateOperation(
        int sequence,
        string actionType,
        Guid? targetId,
        object parameters)
    {
        return new ProposalOperationDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            sequence,
            actionType,
            "card",
            targetId?.ToString(),
            JsonSerializer.Serialize(parameters),
            Guid.NewGuid().ToString(),
            null);
    }
}
