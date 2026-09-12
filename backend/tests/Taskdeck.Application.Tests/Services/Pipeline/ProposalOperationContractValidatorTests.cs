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
    public async Task WorkItemType_RequiresValidTypeCurrentVersionAndOneWrite()
    {
        var boardId = Guid.NewGuid();
        var card = new Card(boardId, Guid.NewGuid(), "Type proposal");
        var unit = new Mock<IUnitOfWork>();
        var cards = new Mock<ICardRepository>();
        unit.Setup(u => u.Cards).Returns(cards.Object);
        cards.Setup(r => r.GetByIdAsync(card.Id, It.IsAny<CancellationToken>())).ReturnsAsync(card);
        var valid = CreateOperation(0, "update", card.Id, new { cardId = card.Id, workItemType = "Epic", expectedUpdatedAt = card.UpdatedAt });
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId, [valid])).IsSuccess.Should().BeTrue();
        var missing = CreateOperation(0, "update", card.Id, new { cardId = card.Id, workItemType = "Epic" });
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId, [missing])).ErrorCode.Should().Be(ErrorCodes.ValidationError);
        var invalid = CreateOperation(0, "update", card.Id, new { cardId = card.Id, workItemType = "Question", expectedUpdatedAt = card.UpdatedAt });
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId, [invalid])).ErrorCode.Should().Be(ErrorCodes.ValidationError);
        var stale = CreateOperation(0, "update", card.Id, new { cardId = card.Id, workItemType = "Spike", expectedUpdatedAt = card.UpdatedAt.AddMinutes(-1) });
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId, [stale])).ErrorCode.Should().Be(ErrorCodes.Conflict);
        var second = CreateOperation(1, "update", card.Id, new { cardId = card.Id, title = "Other" });
        foreach (var operations in new[] { new[] { valid, second }, new[] { second, valid } })
            (await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId, operations)).ErrorCode.Should().Be(ErrorCodes.ValidationError);
        card.WorkItemType.Should().Be(Taskdeck.Domain.Enums.CardWorkItemType.Task);
    }

    [Fact]
    public async Task WorkItemType_IsRejectedOnCardActionsThatDoNotApplyIt()
    {
        // #2950: only the create/update card handlers read 'workItemType'. A move carrying an
        // extra workItemType passed this gate, and the approval preview then announced a
        // "Work item type: Task -> Epic" transition the move never performs. Preview and Apply
        // share this validator, so rejecting the unsupported parameter keeps both honest.
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

        var moveWithType = CreateOperation(
            0, "move", card.Id, new { cardId = card.Id, columnId = targetColumn.Id, workItemType = "Epic" });
        var rejectedMove = await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, boardId, [moveWithType]);
        rejectedMove.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        rejectedMove.ErrorMessage.Should().Be("Parameter 'workItemType' is not supported by card action 'move'");

        var archiveWithType = CreateOperation(0, "archive", card.Id, new { cardId = card.Id, workItemType = "Epic" });
        var rejectedArchive = await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, boardId, [archiveWithType]);
        rejectedArchive.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        rejectedArchive.ErrorMessage.Should().Be("Parameter 'workItemType' is not supported by card action 'archive'");

        // The supported shapes are untouched: a plain move still validates, and an update that
        // really does apply the type still passes with its pinned card version.
        var plainMove = CreateOperation(0, "move", card.Id, new { cardId = card.Id, columnId = targetColumn.Id });
        (await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, boardId, [plainMove]))
            .IsSuccess.Should().BeTrue();
        var typedUpdate = CreateOperation(
            0, "update", card.Id, new { cardId = card.Id, workItemType = "Epic", expectedUpdatedAt = card.UpdatedAt });
        (await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, boardId, [typedUpdate]))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Lifecycle_RejectsStaleApprovalMissingTimestampAndMixedWrites_AndAcceptsCurrentVersion()
    {
        var boardId = Guid.NewGuid();
        var board = new Board("Hierarchy");
        boardId = board.Id;
        var card = new Card(boardId, Guid.NewGuid(), "Lifecycle validation");
        var unit = new Mock<IUnitOfWork>();
        var cards = new Mock<ICardRepository>();
        unit.Setup(u => u.Cards).Returns(cards.Object);
        cards.Setup(r => r.GetByIdAsync(card.Id, It.IsAny<CancellationToken>())).ReturnsAsync(card);
        var boards = new Mock<IBoardRepository>();
        unit.SetupGet(u => u.Boards).Returns(boards.Object);
        boards.Setup(r => r.GetByIdAsync(boardId, It.IsAny<CancellationToken>())).ReturnsAsync(board);
        cards.Setup(r => r.GetHierarchyByBoardIdAsync(boardId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { card });
        var valid = CreateOperation(0, "archive-lifecycle", card.Id, new { cardId = card.Id, expectedUpdatedAt = card.UpdatedAt });
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId, [valid])).IsSuccess.Should().BeTrue();
        var missing = CreateOperation(0, "archive-lifecycle", card.Id, new { cardId = card.Id });
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId, [missing])).ErrorCode.Should().Be(ErrorCodes.ValidationError);
        var stale = CreateOperation(0, "archive-lifecycle", card.Id, new { cardId = card.Id, expectedUpdatedAt = card.UpdatedAt.AddMinutes(-1) });
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId, [stale])).ErrorCode.Should().Be(ErrorCodes.Conflict);
        var second = CreateOperation(1, "archive", card.Id, new { cardId = card.Id });
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId, [valid, second])).ErrorCode.Should().Be(ErrorCodes.ValidationError);
        card.Archive();
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId, [second])).ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
    }
    [Theory]
    [InlineData("archive-lifecycle")]
    [InlineData("restore-lifecycle")]
    public async Task Lifecycle_RejectsTargetIdThatDisagreesWithParameterCardId(string action)
    {
        // #2939: ExecutionAuditRecorder keys the single lifecycle receipt on operation.TargetId
        // while the handler mutates parameters.cardId, so the two must be proven to agree. This
        // pins the shared identity-agreement guard for both lifecycle actions; loosening it would
        // let one card be archived while the receipt landed on an unrelated id.
        var board = new Board("Lifecycle target binding");
        var boardId = board.Id;
        var column = new Column(boardId, "Now", 0);
        var card = new Card(boardId, column.Id, "Real lifecycle target");
        var decoy = new Card(boardId, column.Id, "Unrelated receipt target");
        if (action == "restore-lifecycle") card.Archive();
        var unit = new Mock<IUnitOfWork>();
        var cards = new Mock<ICardRepository>();
        var columns = new Mock<IColumnRepository>();
        unit.Setup(u => u.Columns).Returns(columns.Object);
        columns.Setup(r => r.GetByIdWithCardsAsync(column.Id, It.IsAny<CancellationToken>())).ReturnsAsync(column);
        unit.Setup(u => u.Cards).Returns(cards.Object);
        cards.Setup(r => r.GetByIdAsync(card.Id, It.IsAny<CancellationToken>())).ReturnsAsync(card);
        cards.Setup(r => r.GetByIdAsync(decoy.Id, It.IsAny<CancellationToken>())).ReturnsAsync(decoy);
        cards.Setup(r => r.GetHierarchyByBoardIdAsync(boardId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { card, decoy });
        var boards = new Mock<IBoardRepository>();
        unit.SetupGet(u => u.Boards).Returns(boards.Object);
        boards.Setup(r => r.GetByIdAsync(boardId, It.IsAny<CancellationToken>())).ReturnsAsync(board);

        object parameters = new { cardId = card.Id, expectedUpdatedAt = card.UpdatedAt };

        var mismatched = CreateOperation(0, action, decoy.Id, parameters);
        var mismatchResult = await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId, [mismatched]);
        mismatchResult.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        mismatchResult.ErrorMessage.Should().Contain("targetId must match parameter 'cardId'");

        var matching = CreateOperation(0, action, card.Id, parameters);
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId, [matching]))
            .IsSuccess.Should().BeTrue();

        // With no typed targetId the recorder falls back to parameters.cardId, so there is
        // nothing to disagree with and the operation stays valid.
        var noTargetId = CreateOperation(0, action, null, parameters);
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId, [noTargetId]))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TypedRelation_NormalizesDependsOnAndAllowsAnEarlierPreallocatedCreate()
    {
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "Now", 0);
        var createdId = Guid.NewGuid();
        var existing = new Card(boardId, column.Id, "Existing target");
        var unit = new Mock<IUnitOfWork>();
        var cards = new Mock<ICardRepository>();
        var columns = new Mock<IColumnRepository>();
        unit.Setup(instance => instance.Cards).Returns(cards.Object);
        unit.Setup(instance => instance.Columns).Returns(columns.Object);
        cards.Setup(repository => repository.GetByIdAsync(createdId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Card?)null);
        cards.Setup(repository => repository.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        columns.Setup(repository => repository.GetByIdAsync(column.Id, It.IsAny<CancellationToken>())).ReturnsAsync(column);

        var create = CreateOperation(0, "create", createdId,
            new { boardId, columnId = column.Id, title = "Created before relation" });
        var relation = CreateOperation(1, "add-relation", createdId,
            new { boardId, cardId = createdId, relatedCardId = existing.Id, relationType = "depends-on", expectedRevision = 0L });

        var parsed = JsonSerializer.Deserialize<JsonElement>(relation.Parameters);
        OperationParameterParser.TryGetRelationOperationParameters(parsed, out var normalized, out var error).Should().BeTrue(error);
        normalized.Relation.Should().Be(new CardRelationEdge(existing.Id, createdId, "blocks"));

        var result = await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId, [relation, create]);
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task TypedRelation_RequiresRevisionRejectsArchivedCrossBoardAndLifecycleMixes()
    {
        var boardId = Guid.NewGuid();
        var column = new Column(boardId, "Now", 0);
        var source = new Card(boardId, column.Id, "Source");
        var archived = new Card(boardId, column.Id, "Archived");
        archived.Archive();
        var otherBoardCard = new Card(Guid.NewGuid(), Guid.NewGuid(), "Other board");
        var unit = new Mock<IUnitOfWork>();
        var cards = new Mock<ICardRepository>();
        unit.Setup(instance => instance.Cards).Returns(cards.Object);
        cards.Setup(repository => repository.GetByIdAsync(source.Id, It.IsAny<CancellationToken>())).ReturnsAsync(source);
        cards.Setup(repository => repository.GetByIdAsync(archived.Id, It.IsAny<CancellationToken>())).ReturnsAsync(archived);
        cards.Setup(repository => repository.GetByIdAsync(otherBoardCard.Id, It.IsAny<CancellationToken>())).ReturnsAsync(otherBoardCard);

        var missingPin = CreateOperation(0, "add-relation", source.Id,
            new { boardId, cardId = source.Id, relatedCardId = archived.Id, relationType = "blocks" });
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId, [missingPin]))
            .ErrorMessage.Should().Contain("expectedRevision");

        var archivedRelation = CreateOperation(0, "add-relation", source.Id,
            new { boardId, cardId = source.Id, relatedCardId = archived.Id, relationType = "blocks", expectedRevision = 0L });
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId, [archivedRelation]))
            .ErrorCode.Should().Be(ErrorCodes.InvalidOperation);

        var crossBoardRelation = CreateOperation(0, "add-relation", source.Id,
            new { boardId, cardId = source.Id, relatedCardId = otherBoardCard.Id, relationType = "blocks", expectedRevision = 0L });
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId, [crossBoardRelation]))
            .ErrorCode.Should().Be(ErrorCodes.Forbidden);

        var lifecycle = CreateOperation(1, "archive-lifecycle", source.Id,
            new { cardId = source.Id, expectedUpdatedAt = source.UpdatedAt });
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, boardId,
            [crossBoardRelation with { Parameters = JsonSerializer.Serialize(new { boardId, cardId = source.Id, relatedCardId = source.Id, relationType = "blocks", expectedRevision = 0L }) }, lifecycle]))
            .ErrorMessage.Should().Contain("cannot be combined");
    }

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

    [Fact]
    public async Task Restore_RejectsASecondLifecycleOperationInTheSameProposal()
    {
        // #2926's originating trigger - restore(A) + restore(B) into a WIP-1 column - is closed by
        // ProposalHierarchyValidator, which admits at most one hierarchy-affecting operation per
        // proposal, so the pair never reaches the cumulative WIP projection below. This pins that
        // gate: if batch lifecycle proposals are ever enabled, the projection in
        // BoardValidationContext is what has to keep the WIP contract honest, and the
        // archive/restore deltas it already carries are why relaxing this gate stays safe.
        var fixtureBoard = new Board("Cumulative restore");
        var boardId = fixtureBoard.Id;
        var column = new Column(boardId, "Now", 0, wipLimit: 1);
        var first = new Card(boardId, column.Id, "Archived A");
        var second = new Card(boardId, column.Id, "Archived B");
        first.Archive();
        second.Archive();
        column.AddCard(first);
        column.AddCard(second);
        var unitOfWork = CreateLifecycleMocks(fixtureBoard, [column], [first, second]);

        var restoreFirst = CreateOperation(0, "restore-lifecycle", first.Id,
            new { cardId = first.Id, expectedUpdatedAt = first.UpdatedAt });
        var restoreSecond = CreateOperation(1, "restore-lifecycle", second.Id,
            new { cardId = second.Id, expectedUpdatedAt = second.UpdatedAt });

        var combined = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, [restoreFirst, restoreSecond]);
        combined.IsSuccess.Should().BeFalse();
        combined.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        combined.ErrorMessage.Should().Contain("separate proposal for each hierarchy-affecting operation");

        // Control: a single restore into the same empty WIP-1 column is still valid.
        (await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, boardId, [restoreFirst]))
            .IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Restore_CountsAPrecedingCardThatOccupiesTheOriginalColumn(bool fillByCreate)
    {
        // #2926: Apply runs operations in Sequence order, so a create into - or a move into - the
        // restore target column takes the last WIP slot before the restore handler runs. Validating
        // every restore against the proposal's starting count let that pass preview and fail at
        // execute, rolling the whole proposal back. Preview must now reject it with the existing
        // WipLimitExceeded shape.
        var fixtureBoard = new Board("Preceding occupant");
        var boardId = fixtureBoard.Id;
        var column = new Column(boardId, "Now", 0, wipLimit: 1);
        var otherColumn = new Column(boardId, "Later", 1);
        var archived = new Card(boardId, column.Id, "Archived A");
        archived.Archive();
        column.AddCard(archived);
        var mover = new Card(boardId, otherColumn.Id, "Moves in");
        otherColumn.AddCard(mover);
        var unitOfWork = CreateLifecycleMocks(fixtureBoard, [column, otherColumn], [archived, mover]);

        var occupy = fillByCreate
            ? CreateOperation(0, "create", null, new { boardId, columnId = column.Id, title = "Takes the slot" })
            : CreateOperation(0, "move", mover.Id, new { cardId = mover.Id, columnId = column.Id });
        var restore = CreateOperation(1, "restore-lifecycle", archived.Id,
            new { cardId = archived.Id, expectedUpdatedAt = archived.UpdatedAt });

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, [occupy, restore]);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.WipLimitExceeded);
        result.ErrorMessage.Should().Contain("original column is full");

        // With restore first, the following create/move is the operation that breaches WIP.
        var reordered = new[]
        {
            CreateOperation(0, "restore-lifecycle", archived.Id,
                new { cardId = archived.Id, expectedUpdatedAt = archived.UpdatedAt }),
            fillByCreate
                ? CreateOperation(1, "create", null, new { boardId, columnId = column.Id, title = "Takes the slot" })
                : CreateOperation(1, "move", mover.Id, new { cardId = mover.Id, columnId = column.Id })
        };
        var reorderedResult = await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, boardId, reordered);
        reorderedResult.ErrorCode.Should().Be(ErrorCodes.WipLimitExceeded);
        reorderedResult.ErrorMessage.Should().Contain(fillByCreate ? "Cannot add card" : "Cannot move card");
    }

    [Fact]
    public async Task Restore_CountsTheSlotFreedByAPrecedingMoveOutOfTheOriginalColumn()
    {
        // The same ordered projection has to release capacity as well as consume it, or a valid
        // "make room, then restore" proposal would be rejected at preview even though Apply would
        // accept it. Free the only WIP-1 slot by moving its occupant away, then restore.
        var fixtureBoard = new Board("Freed slot");
        var boardId = fixtureBoard.Id;
        var column = new Column(boardId, "Now", 0, wipLimit: 1);
        var elsewhere = new Column(boardId, "Later", 1);
        var occupant = new Card(boardId, column.Id, "Occupant");
        var archived = new Card(boardId, column.Id, "Archived A");
        archived.Archive();
        column.AddCard(occupant);
        column.AddCard(archived);
        var unitOfWork = CreateLifecycleMocks(fixtureBoard, [column, elsewhere], [occupant, archived]);

        var moveOut = CreateOperation(0, "move", occupant.Id, new { cardId = occupant.Id, columnId = elsewhere.Id });
        var restore = CreateOperation(1, "restore-lifecycle", archived.Id,
            new { cardId = archived.Id, expectedUpdatedAt = archived.UpdatedAt });

        (await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, boardId, [moveOut, restore]))
            .IsSuccess.Should().BeTrue();

        // Without the move the column is genuinely full, and the restore is rejected as before.
        (await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, boardId, [restore]))
            .ErrorCode.Should().Be(ErrorCodes.WipLimitExceeded);
    }

    [Fact]
    public async Task Restore_DoesNotCountASlotFreedByAMoveApplyWouldReject()
    {
        // Raised independently by the Codex review of #3019 and by the fresh-context review.
        // Only a move Apply can actually perform frees its source slot. Column C (limit 1) holds
        // active X plus archived A; column D (limit 1) is already full. "Move X to D, then restore
        // A" must stay refused: CardService.MoveCardAsync rejects the move at D's WIP check, so the
        // slot in C is never freed and approving this proposal would just move the failure to
        // execute - the exact shape #2926 exists to remove.
        var fixtureBoard = new Board("Impossible move");
        var boardId = fixtureBoard.Id;
        var column = new Column(boardId, "Now", 0, wipLimit: 1);
        var full = new Column(boardId, "Later", 1, wipLimit: 1);
        var occupant = new Card(boardId, column.Id, "Occupant");
        var archived = new Card(boardId, column.Id, "Archived A");
        archived.Archive();
        column.AddCard(occupant);
        column.AddCard(archived);
        var blocker = new Card(boardId, full.Id, "Already there");
        full.AddCard(blocker);
        var unitOfWork = CreateLifecycleMocks(fixtureBoard, [column, full], [occupant, archived, blocker]);

        var impossibleMove = CreateOperation(0, "move", occupant.Id, new { cardId = occupant.Id, columnId = full.Id });
        var restore = CreateOperation(1, "restore-lifecycle", archived.Id,
            new { cardId = archived.Id, expectedUpdatedAt = archived.UpdatedAt });

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, [impossibleMove, restore]);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.WipLimitExceeded);
        result.ErrorMessage.Should().Contain("Cannot move card").And.Contain("Later");

        // Control: the same move into a column with room does free the slot.
        var roomy = new Column(boardId, "Roomy", 2, wipLimit: 5);
        var roomyUnitOfWork = CreateLifecycleMocks(fixtureBoard, [column, roomy], [occupant, archived]);
        var possibleMove = CreateOperation(0, "move", occupant.Id, new { cardId = occupant.Id, columnId = roomy.Id });
        (await ProposalOperationContractValidator.ValidateAsync(
                roomyUnitOfWork.Object, boardId, [possibleMove, restore]))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Restore_AllowsAPrecedingOccupantWhenTheWipLimitStillHasRoom()
    {
        // Valid multi-operation control: the projection must not turn a proposal that fits into a
        // false rejection. Limit 2, one active card, one create and one restore into the same
        // column - Apply ends at exactly the limit, so preview accepts it.
        var fixtureBoard = new Board("Within limit");
        var boardId = fixtureBoard.Id;
        var column = new Column(boardId, "Now", 0, wipLimit: 2);
        var archived = new Card(boardId, column.Id, "Archived A");
        archived.Archive();
        column.AddCard(archived);
        var unitOfWork = CreateLifecycleMocks(fixtureBoard, [column], [archived]);

        var create = CreateOperation(0, "create", null, new { boardId, columnId = column.Id, title = "Also fits" });
        var restore = CreateOperation(1, "restore-lifecycle", archived.Id,
            new { cardId = archived.Id, expectedUpdatedAt = archived.UpdatedAt });

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, [create, restore]);
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task Restore_IgnoresOccupancyChangesInOtherColumnsAndUnlimitedColumns()
    {
        // The projection is per column and only applies where a WIP limit exists: a create into a
        // different column must not consume the restore column's capacity.
        var fixtureBoard = new Board("Scoped projection");
        var boardId = fixtureBoard.Id;
        var column = new Column(boardId, "Now", 0, wipLimit: 1);
        var elsewhere = new Column(boardId, "Later", 1);
        var archived = new Card(boardId, column.Id, "Archived A");
        archived.Archive();
        column.AddCard(archived);
        var unitOfWork = CreateLifecycleMocks(fixtureBoard, [column, elsewhere], [archived]);

        var createElsewhere = CreateOperation(0, "create", null,
            new { boardId, columnId = elsewhere.Id, title = "Unrelated column" });
        var restore = CreateOperation(1, "restore-lifecycle", archived.Id,
            new { cardId = archived.Id, expectedUpdatedAt = archived.UpdatedAt });

        var result = await ProposalOperationContractValidator.ValidateAsync(
            unitOfWork.Object, boardId, [createElsewhere, restore]);
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Capacity_CreateAndMoveRejectTheOperationThatExceedsOrderedOccupancy(bool fillByCreate)
    {
        var board = new Board("Ordered capacity");
        var target = new Column(board.Id, "Limited", 0, wipLimit: 1);
        var source = new Column(board.Id, "Source", 1);
        var mover = new Card(board.Id, source.Id, "Moves in");
        source.AddCard(mover);
        var unitOfWork = CreateLifecycleMocks(board, [target, source], [mover]);
        var first = CreateOperation(0, "create", null,
            new { boardId = board.Id, columnId = target.Id, title = "First" });
        var second = fillByCreate
            ? CreateOperation(1, "create", null, new { boardId = board.Id, columnId = target.Id, title = "Second" })
            : CreateOperation(1, "move", mover.Id, new { cardId = mover.Id, columnId = target.Id });

        // Deliberately supply the list backwards: Apply uses Sequence, not input order.
        var result = await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, board.Id, [second, first]);

        result.ErrorCode.Should().Be(ErrorCodes.WipLimitExceeded);
        result.ErrorMessage.Should().Contain(fillByCreate ? "Cannot add card" : "Cannot move card").And.Contain("Limited");
        (await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, board.Id, [second]))
            .IsSuccess.Should().BeTrue("one incoming card fits");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Capacity_ArchiveFreesSpaceBeforeCreateOrMove(bool fillByCreate)
    {
        var board = new Board("Archive frees capacity");
        var target = new Column(board.Id, "Limited", 0, wipLimit: 1);
        var source = new Column(board.Id, "Source", 1);
        var occupant = new Card(board.Id, target.Id, "Archive me");
        var mover = new Card(board.Id, source.Id, "Moves in");
        target.AddCard(occupant);
        source.AddCard(mover);
        var unitOfWork = CreateLifecycleMocks(board, [target, source], [occupant, mover]);
        var archive = CreateOperation(0, "archive-lifecycle", occupant.Id,
            new { cardId = occupant.Id, expectedUpdatedAt = occupant.UpdatedAt, detachChildren = true });
        var incoming = fillByCreate
            ? CreateOperation(1, "create", null, new { boardId = board.Id, columnId = target.Id, title = "Replacement" })
            : CreateOperation(1, "move", mover.Id, new { cardId = mover.Id, columnId = target.Id });

        (await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, board.Id, [archive, incoming]))
            .IsSuccess.Should().BeTrue();
        (await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, board.Id, [incoming]))
            .ErrorCode.Should().Be(ErrorCodes.WipLimitExceeded);
    }

    [Fact]
    public async Task Capacity_SameColumnMoveAndUnlimitedColumnKeepTheirExistingContract()
    {
        var board = new Board("Unchanged controls");
        var target = new Column(board.Id, "Limited", 0, wipLimit: 1);
        var unlimited = new Column(board.Id, "Unlimited", 1);
        var occupant = new Card(board.Id, target.Id, "Existing");
        target.AddCard(occupant);
        var unitOfWork = CreateLifecycleMocks(board, [target, unlimited], [occupant]);
        var sameColumn = CreateOperation(0, "move", occupant.Id, new { cardId = occupant.Id, columnId = target.Id });
        var create = CreateOperation(1, "create", null, new { boardId = board.Id, columnId = unlimited.Id, title = "New" });
        var move = CreateOperation(2, "move", occupant.Id, new { cardId = occupant.Id, columnId = unlimited.Id });

        (await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, board.Id, [sameColumn, create, move]))
            .IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task Capacity_DeleteFreesOnlyAnActiveSlot(bool archived, bool create)
    {
        var board = new Board("Delete capacity");
        var target = new Column(board.Id, "Limited", 0, wipLimit: 1);
        var source = new Column(board.Id, "Source", 1);
        var deleted = new Card(board.Id, target.Id, "Delete me");
        if (archived) deleted.Archive();
        target.AddCard(deleted);
        var occupant = new Card(board.Id, target.Id, "Active occupant");
        if (archived) target.AddCard(occupant);
        var mover = new Card(board.Id, source.Id, "Mover");
        source.AddCard(mover);
        var unitOfWork = CreateLifecycleMocks(board, [target, source], [deleted, mover, occupant]);
        var delete = CreateOperation(0, "delete", deleted.Id,
            new { cardId = deleted.Id, expectedUpdatedAt = deleted.UpdatedAt, detachChildren = true });
        var incoming = create
            ? CreateOperation(1, "create", null, new { boardId = board.Id, columnId = target.Id, title = "Replacement" })
            : CreateOperation(1, "move", mover.Id, new { cardId = mover.Id, columnId = target.Id });

        var result = await ProposalOperationContractValidator.ValidateAsync(unitOfWork.Object, board.Id, [delete, incoming]);

        if (archived)
            result.ErrorCode.Should().Be(ErrorCodes.WipLimitExceeded);
        else
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    private static Mock<IUnitOfWork> CreateLifecycleMocks(Board board, Column[] columns, Card[] cards)
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var cardRepository = new Mock<ICardRepository>();
        var columnRepository = new Mock<IColumnRepository>();
        var boardRepository = new Mock<IBoardRepository>();
        unitOfWork.Setup(instance => instance.Cards).Returns(cardRepository.Object);
        unitOfWork.Setup(instance => instance.Columns).Returns(columnRepository.Object);
        unitOfWork.SetupGet(instance => instance.Boards).Returns(boardRepository.Object);
        boardRepository.Setup(repository => repository.GetByIdAsync(board.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);
        cardRepository.Setup(repository => repository.GetHierarchyByBoardIdAsync(board.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cards);
        foreach (var card in cards)
        {
            cardRepository.Setup(repository => repository.GetByIdAsync(card.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(card);
        }
        foreach (var column in columns)
        {
            columnRepository.Setup(repository => repository.GetByIdAsync(column.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(column);
            columnRepository.Setup(repository => repository.GetByIdWithCardsAsync(column.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(column);
        }
        return unitOfWork;
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
