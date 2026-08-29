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

    [Fact]
    public async Task ValidateAsync_ShouldAllowReferencesToCardsCreatedEarlierBySequence()
    {
        var boardId = Guid.NewGuid();
        var sourceColumn = new Column(boardId, "Backlog", 0);
        var targetColumn = new Column(boardId, "Done", 1);
        var createdCardId = Guid.NewGuid();
        var label = new Label(boardId, "urgent", "#FF0000");
        var unitOfWork = new Mock<IUnitOfWork>();
        var cards = new Mock<ICardRepository>();
        var columns = new Mock<IColumnRepository>();
        var labels = new Mock<ILabelRepository>();
        unitOfWork.Setup(instance => instance.Cards).Returns(cards.Object);
        unitOfWork.Setup(instance => instance.Columns).Returns(columns.Object);
        unitOfWork.Setup(instance => instance.Labels).Returns(labels.Object);
        columns.Setup(repository => repository.GetByIdAsync(sourceColumn.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceColumn);
        columns.Setup(repository => repository.GetByIdAsync(targetColumn.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetColumn);
        labels.Setup(repository => repository.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { label });

        // Deliberately supply the list out of order. Apply is sequence-ordered, so
        // shared validation must use the same ordering contract.
        var operations = new[]
        {
            CreateOperation(3, "add-label", createdCardId, new { cardId = createdCardId, labelId = label.Id }),
            CreateOperation(2, "move", createdCardId, new { cardId = createdCardId, columnId = targetColumn.Id }),
            CreateOperation(1, "update", createdCardId, new { cardId = createdCardId, title = "Ready" }),
            CreateOperation(
                0,
                "create",
                createdCardId,
                new { boardId, columnId = sourceColumn.Id, title = "New card" })
        };

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, operations);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        cards.Verify(
            repository => repository.GetByIdAsync(createdCardId, It.IsAny<CancellationToken>()),
            Times.Once,
            "the create target is checked for collisions once, then later references use the planned-card set");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectCardArchiveAfterEarlierBoardArchive()
    {
        var boardId = Guid.NewGuid();
        var card = new Card(boardId, Guid.NewGuid(), "File release notes");
        var unitOfWork = new Mock<IUnitOfWork>();
        var cards = new Mock<ICardRepository>();
        unitOfWork.Setup(instance => instance.Cards).Returns(cards.Object);
        cards.Setup(repository => repository.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        var operations = new[]
        {
            CreateOperation(1, "archive", card.Id, new { cardId = card.Id }),
            CreateOperation(0, "update", boardId, new { boardId, isArchived = true }, targetType: "board")
        };

        var result = await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, boardId, operations);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        result.ErrorMessage.Should().Be(
            "Cannot apply an operation after archiving the proposal board. Restore the board before making further changes.");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectDuplicateCreateCardTargetIds()
    {
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "Backlog", 0);
        var createdCardId = Guid.NewGuid();
        var unitOfWork = new Mock<IUnitOfWork>();
        var cards = new Mock<ICardRepository>();
        var columns = new Mock<IColumnRepository>();
        unitOfWork.Setup(instance => instance.Cards).Returns(cards.Object);
        unitOfWork.Setup(instance => instance.Columns).Returns(columns.Object);
        cards.Setup(repository => repository.GetByIdAsync(createdCardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Card?)null);
        columns.Setup(repository => repository.GetByIdAsync(column.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);
        var operations = new[]
        {
            CreateOperation(
                0,
                "create",
                createdCardId,
                new { boardId, columnId = column.Id, title = "First card" }),
            CreateOperation(
                1,
                "create",
                createdCardId,
                new { boardId, columnId = column.Id, title = "Duplicate card" })
        };

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, operations);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("duplicated");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectCreateCardWhoseIdCollidesWithExistingCard_EvenWhenCardIdParameterMatchesTargetId()
    {
        // A create-card op that also carries a cardId parameter equal to its targetId must
        // NOT be routed through the existing-card branch. Before #1370 that branch treated
        // the colliding id as a valid reference (the card exists on the board), the op
        // previewed OK and was registered as planned, then Apply blew up creating a card
        // with a duplicate id. Preview must reject the collision up front.
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "Backlog", 0);
        var existingCard = new Card(boardId, column.Id, "Existing");
        var (unitOfWork, cards, columns, _) = CreateMocks();
        cards.Setup(repository => repository.GetByIdAsync(existingCard.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCard);
        columns.Setup(repository => repository.GetByIdAsync(column.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);
        var operations = new[]
        {
            CreateOperation(
                0,
                "create",
                existingCard.Id,
                new { boardId, columnId = column.Id, title = "New card", cardId = existingCard.Id })
        };

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, operations);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("already exists");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectCardReferenceBeforeItsCreateSequence()
    {
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "Backlog", 0);
        var createdCardId = Guid.NewGuid();
        var unitOfWork = new Mock<IUnitOfWork>();
        var cards = new Mock<ICardRepository>();
        var columns = new Mock<IColumnRepository>();
        unitOfWork.Setup(instance => instance.Cards).Returns(cards.Object);
        unitOfWork.Setup(instance => instance.Columns).Returns(columns.Object);
        cards.Setup(repository => repository.GetByIdAsync(createdCardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Card?)null);
        columns.Setup(repository => repository.GetByIdAsync(column.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);
        var operations = new[]
        {
            CreateOperation(
                1,
                "create",
                createdCardId,
                new { boardId, columnId = column.Id, title = "New card" }),
            CreateOperation(0, "update", createdCardId, new { cardId = createdCardId, title = "Too early" })
        };

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, operations);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.ErrorMessage.Should().Contain("outside the proposal board scope");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRequireColumnIdForMoveCardApplyContract()
    {
        var boardId = Guid.NewGuid();
        var sourceColumn = new Column(boardId, "Backlog", 0);
        var targetColumn = new Column(boardId, "Done", 1);
        var card = new Card(boardId, sourceColumn.Id, "Move me");
        var unitOfWork = new Mock<IUnitOfWork>();
        var cards = new Mock<ICardRepository>();
        var columns = new Mock<IColumnRepository>();
        unitOfWork.Setup(instance => instance.Cards).Returns(cards.Object);
        unitOfWork.Setup(instance => instance.Columns).Returns(columns.Object);
        cards.Setup(repository => repository.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        columns.Setup(repository => repository.GetByIdAsync(targetColumn.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetColumn);
        var operation = CreateOperation(
            0,
            "move",
            card.Id,
            new { cardId = card.Id, targetColumnId = targetColumn.Id });

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, new[] { operation });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Be("Missing required parameter 'columnId'");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRequirePositionForColumnReorder()
    {
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "Backlog", 0);
        var unitOfWork = new Mock<IUnitOfWork>();
        var columns = new Mock<IColumnRepository>();
        unitOfWork.Setup(instance => instance.Columns).Returns(columns.Object);
        columns.Setup(repository => repository.GetByIdAsync(column.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);
        var operation = CreateOperation(
            0,
            "reorder",
            column.Id,
            new { columnId = column.Id },
            targetType: "column");

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, new[] { operation });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Be("Missing required parameter 'position'");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectNegativePositionForColumnReorder()
    {
        // Mirrors the apply-side guard (ColumnService.ReorderColumnAsync and the
        // operation handler both reject negative positions) so an impossible
        // destination fails at preview, not after approval.
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "Backlog", 0);
        var unitOfWork = new Mock<IUnitOfWork>();
        var columns = new Mock<IColumnRepository>();
        unitOfWork.Setup(instance => instance.Columns).Returns(columns.Object);
        columns.Setup(repository => repository.GetByIdAsync(column.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);
        var operation = CreateOperation(
            0,
            "reorder",
            column.Id,
            new { columnId = column.Id, position = -1 },
            targetType: "column");

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, new[] { operation });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Be("Invalid position: must be non-negative");
    }

    [Fact]
    public async Task ValidateAsync_ShouldAcceptCanonicalCreateColumnContract()
    {
        var boardId = Guid.NewGuid();
        var (unitOfWork, _, columns, _) = CreateMocks();
        columns.Setup(repository => repository.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Column>());
        var operation = CreateOperation(
            0,
            "create",
            null,
            new { boardId, name = "Review", position = 3, wipLimit = 2 },
            targetType: "column");

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object,
            boardId,
            new[] { operation });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    [Theory]
    [InlineData("{\"name\":\"Review\",\"position\":1}", "'boardId'")]
    [InlineData("{\"boardId\":\"00000000-0000-0000-0000-000000000001\",\"position\":1}", "'name'")]
    [InlineData("{\"boardId\":\"00000000-0000-0000-0000-000000000001\",\"name\":\"Review\"}", "'position'")]
    [InlineData("{\"boardId\":\"00000000-0000-0000-0000-000000000001\",\"name\":\"Review\",\"position\":-1}", "non-negative")]
    [InlineData("{\"boardId\":\"00000000-0000-0000-0000-000000000001\",\"name\":\"Review\",\"position\":1.5}", "integer")]
    [InlineData("{\"boardId\":\"00000000-0000-0000-0000-000000000001\",\"name\":\"Review\",\"position\":1,\"wipLimit\":0}", "greater than 0")]
    [InlineData("{\"boardId\":\"00000000-0000-0000-0000-000000000001\",\"name\":\"Review\",\"position\":1,\"extra\":true}", "Unsupported")]
    public async Task ValidateAsync_ShouldRejectMalformedCreateColumnContract(string rawParameters, string expectedMessage)
    {
        var operation = new ProposalOperationDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            0,
            "create",
            "column",
            null,
            rawParameters,
            Guid.NewGuid().ToString(),
            null);
        var unitOfWork = new Mock<IUnitOfWork>();

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object,
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            new[] { operation });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain(expectedMessage);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectCreateColumnTargetIdAndCrossBoardRedirect()
    {
        var boardId = Guid.NewGuid();
        var otherBoardId = Guid.NewGuid();
        var unitOfWork = new Mock<IUnitOfWork>();
        var targetIdOperation = CreateOperation(
            0,
            "create",
            Guid.NewGuid(),
            new { boardId, name = "Review", position = 1 },
            targetType: "column");
        var redirectOperation = CreateOperation(
            0,
            "create",
            null,
            new { boardId = otherBoardId, name = "Review", position = 1 },
            targetType: "column");

        var targetResult = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object,
            boardId,
            new[] { targetIdOperation });
        var redirectResult = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object,
            boardId,
            new[] { redirectOperation });

        targetResult.IsSuccess.Should().BeFalse();
        targetResult.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        targetResult.ErrorMessage.Should().Contain("must not specify targetId");
        redirectResult.IsSuccess.Should().BeFalse();
        redirectResult.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        redirectResult.ErrorMessage.Should().Contain("outside the proposal board scope");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectCreateColumnNameBeyondDomainLimit()
    {
        var boardId = Guid.NewGuid();
        var unitOfWork = new Mock<IUnitOfWork>();
        var operation = CreateOperation(
            0,
            "create",
            null,
            new { boardId, name = new string('x', 51), position = 1 },
            targetType: "column");

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object,
            boardId,
            new[] { operation });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Column name cannot exceed 50");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectOccupiedCreateColumnPosition()
    {
        var boardId = Guid.NewGuid();
        var existingColumn = new Column(boardId, "Backlog", 0);
        var (unitOfWork, _, columns, _) = CreateMocks();
        columns.Setup(repository => repository.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { existingColumn });
        var operation = CreateOperation(
            0,
            "create",
            null,
            new { boardId, name = "Review", position = 0 },
            targetType: "column");

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object,
            boardId,
            new[] { operation });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("position 0");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectDuplicateCreateColumnPositionsWithinProposal()
    {
        var boardId = Guid.NewGuid();
        var (unitOfWork, _, columns, _) = CreateMocks();
        columns.Setup(repository => repository.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Column>());
        var operations = new[]
        {
            CreateOperation(0, "create", null, new { boardId, name = "Review", position = 1 }, targetType: "column"),
            CreateOperation(1, "create", null, new { boardId, name = "Ready", position = 1 }, targetType: "column")
        };

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object,
            boardId,
            operations);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("duplicated within the proposal");
    }

    [Fact]
    public async Task ValidateAsync_ShouldAllowDuplicateColumnNamesAtDifferentPositions()
    {
        var boardId = Guid.NewGuid();
        var (unitOfWork, _, columns, _) = CreateMocks();
        columns.Setup(repository => repository.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new Column(boardId, "Review", 0) });
        var operation = CreateOperation(
            0,
            "create",
            null,
            new { boardId, name = "review", position = 1 },
            targetType: "column");

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object,
            boardId,
            new[] { operation });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateAsync_ShouldRequireAnUpdateFieldForBoardUpdate()
    {
        var boardId = Guid.NewGuid();
        var unitOfWork = new Mock<IUnitOfWork>();
        var operation = CreateOperation(
            0,
            "update",
            boardId,
            new { boardId },
            targetType: "board");

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, new[] { operation });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("requires at least one");
    }

    [Fact]
    public async Task ValidateAsync_ShouldAcceptValidBoardAndColumnOperations()
    {
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "Backlog", 0);
        var unitOfWork = new Mock<IUnitOfWork>();
        var columns = new Mock<IColumnRepository>();
        unitOfWork.Setup(instance => instance.Columns).Returns(columns.Object);
        columns.Setup(repository => repository.GetByIdAsync(column.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);
        var operations = new[]
        {
            CreateOperation(0, "update", boardId, new { boardId, name = "Renamed" }, targetType: "board"),
            CreateOperation(1, "reorder", column.Id, new { columnId = column.Id, position = 1 }, targetType: "column")
        };

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, operations);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("boardId")]
    [InlineData("columnId")]
    public async Task ValidateAsync_ShouldRequireFieldsThatCreateCardApplyConsumes(string missingParameter)
    {
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "Now", 0);
        var unitOfWork = new Mock<IUnitOfWork>();
        var columns = new Mock<IColumnRepository>();
        unitOfWork.Setup(instance => instance.Columns).Returns(columns.Object);
        columns.Setup(repository => repository.GetByIdAsync(column.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);
        var parameters = new Dictionary<string, object>
        {
            ["title"] = "New card",
            ["boardId"] = boardId,
            ["columnId"] = column.Id
        };
        parameters.Remove(missingParameter);
        var operation = CreateOperation(0, "create", null, parameters);

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, new[] { operation });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain($"'{missingParameter}'");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectCreateCardLabelNamesOutsideProposalBoard()
    {
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "Now", 0);
        var unitOfWork = new Mock<IUnitOfWork>();
        var columns = new Mock<IColumnRepository>();
        var labels = new Mock<ILabelRepository>();
        unitOfWork.Setup(instance => instance.Columns).Returns(columns.Object);
        unitOfWork.Setup(instance => instance.Labels).Returns(labels.Object);
        columns.Setup(repository => repository.GetByIdAsync(column.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);
        labels.Setup(repository => repository.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new Label(boardId, "shopping", "#22C55E") });
        var operation = CreateOperation(
            0,
            "create",
            null,
            new { boardId, columnId = column.Id, title = "Buy milk", labels = new[] { "foreign-board-label" } });

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, new[] { operation });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Contain("proposal board");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectCreateCardWithAmbiguousCaseInsensitiveLabelName()
    {
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "Now", 0);
        var unitOfWork = new Mock<IUnitOfWork>();
        var columns = new Mock<IColumnRepository>();
        var labels = new Mock<ILabelRepository>();
        unitOfWork.Setup(instance => instance.Columns).Returns(columns.Object);
        unitOfWork.Setup(instance => instance.Labels).Returns(labels.Object);
        columns.Setup(repository => repository.GetByIdAsync(column.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);
        labels.Setup(repository => repository.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new Label(boardId, "urgent", "#FF0000"),
                new Label(boardId, "URGENT", "#00FF00")
            });
        var operation = CreateOperation(
            0,
            "create",
            null,
            new { boardId, columnId = column.Id, title = "Review brief", labels = new[] { "urgent" } });

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, new[] { operation });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("ambiguous");
    }

    [Theory]
    [InlineData("update")]
    [InlineData("add-label")]
    public async Task ValidateAsync_ShouldRejectAmbiguousCaseInsensitiveLabelNames(string actionType)
    {
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "Now", 0);
        var card = new Card(boardId, column.Id, "Review proposal");
        var unitOfWork = new Mock<IUnitOfWork>();
        var cards = new Mock<ICardRepository>();
        var labels = new Mock<ILabelRepository>();
        unitOfWork.Setup(instance => instance.Cards).Returns(cards.Object);
        unitOfWork.Setup(instance => instance.Labels).Returns(labels.Object);
        cards.Setup(repository => repository.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        labels.Setup(repository => repository.GetByBoardIdAsync(boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new Label(boardId, "urgent", "#FF0000"),
                new Label(boardId, "URGENT", "#00FF00")
            });
        object parameters = actionType == "update"
            ? new Dictionary<string, object> { ["cardId"] = card.Id, ["labels"] = new[] { "urgent" } }
            : new Dictionary<string, object> { ["cardId"] = card.Id, ["labelName"] = "urgent" };
        var operation = CreateOperation(0, actionType, card.Id, parameters);

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, new[] { operation });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("ambiguous");
    }

    [Theory]
    [InlineData("add--label")]
    [InlineData("add_-label")]
    [InlineData("remove__label")]
    [InlineData("add..label")]
    [InlineData(" add-label ")]
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

    [Fact]
    public async Task ValidateAsync_ShouldRejectCreateCardWithEmptyTargetId()
    {
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "Now", 0);
        var (unitOfWork, _, columns, _) = CreateMocks();
        columns.Setup(repository => repository.GetByIdAsync(column.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);
        var operations = new[]
        {
            CreateOperation(0, "create", Guid.Empty, new { boardId, columnId = column.Id, title = "New card" })
        };

        var result = await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, boardId, operations);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("non-empty");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectCardTitleBeyondDomainLimit()
    {
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "Now", 0);
        var (unitOfWork, _, columns, _) = CreateMocks();
        columns.Setup(repository => repository.GetByIdAsync(column.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(column);
        var operations = new[]
        {
            CreateOperation(
                0,
                "create",
                Guid.NewGuid(),
                new { boardId, columnId = column.Id, title = new string('x', 201) })
        };

        var result = await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, boardId, operations);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Card title cannot exceed 200");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectCardDescriptionBeyondDomainLimit()
    {
        var boardId = Guid.NewGuid();
        var card = new Card(boardId, Guid.NewGuid(), "Existing");
        var (unitOfWork, cards, _, _) = CreateMocks();
        cards.Setup(repository => repository.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(card);
        var operations = new[]
        {
            CreateOperation(0, "update", card.Id, new { cardId = card.Id, description = new string('x', 2001) })
        };

        var result = await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, boardId, operations);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Card description cannot exceed 2000");
    }

    [Fact]
    public async Task ValidateAsync_ShouldRejectBoardNameBeyondDomainLimit()
    {
        var boardId = Guid.NewGuid();
        var (unitOfWork, _, _, _) = CreateMocks();
        var operations = new[]
        {
            CreateOperation(0, "update", boardId, new { boardId, name = new string('x', 101) }, targetType: "board")
        };

        var result = await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, boardId, operations);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Board name cannot exceed 100");
    }

    private static (Mock<IUnitOfWork> unitOfWork, Mock<ICardRepository> cards, Mock<IColumnRepository> columns, Mock<ILabelRepository> labels) CreateMocks()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var cards = new Mock<ICardRepository>();
        var columns = new Mock<IColumnRepository>();
        var labels = new Mock<ILabelRepository>();
        unitOfWork.Setup(instance => instance.Cards).Returns(cards.Object);
        unitOfWork.Setup(instance => instance.Columns).Returns(columns.Object);
        unitOfWork.Setup(instance => instance.Labels).Returns(labels.Object);
        return (unitOfWork, cards, columns, labels);
    }

    private static ProposalOperationDto CreateOperation(
        int sequence,
        string actionType,
        Guid? targetId,
        object parameters,
        string targetType = "card")
    {
        return new ProposalOperationDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            sequence,
            actionType,
            targetType,
            targetId?.ToString(),
            JsonSerializer.Serialize(parameters),
            Guid.NewGuid().ToString(),
            null);
    }
}
