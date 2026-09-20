using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services.Pipeline;

public sealed class ProposalOperationShapeTests
{
    private static readonly Guid BoardId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CardId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ColumnId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Stamp = DateTimeOffset.Parse("2000-01-01T00:00:00Z");

    public static IEnumerable<object[]> MalformedOperations()
    {
        yield return [Card("replace-assignments", new { cardId = CardId })];
        yield return [Card("replace-assignments", new { cardId = CardId, userIds = new[] { Guid.Empty }, expectedUpdatedAt = Stamp })];
        yield return [Card("add-relation", new { cardId = CardId })];
        yield return [Card("remove-relation", new { boardId = BoardId, cardId = CardId, relatedCardId = OtherId, relationType = "blocks", expectedRevision = -1 })];
        yield return [Card("add-label", new { cardId = CardId })];
        yield return [Card("remove-label", new { cardId = CardId, labelId = OtherId, labelName = "both" })];
        yield return [Card("update", new { title = "Missing identity" })];
        yield return [Card("update", new { cardId = OtherId, title = "Disagreeing identity" })];
        yield return [Card("update", new { cardId = CardId, labels = new[] { "one" }, labelIds = new[] { OtherId } })];
        yield return [Card("update", new { cardId = CardId, dueDate = 12 })];
        yield return [Card("update", new { cardId = CardId, dueDate = Stamp, clearDueDate = true })];
        yield return [Card("update", new { cardId = CardId, estimatedEffortMinutes = 5 })];
        yield return [Card("update", new { cardId = CardId, workItemType = "Epic" })];
        yield return [Card("update", new { cardId = CardId, parentCardId = OtherId })];
        yield return [Card("update", new { cardId = CardId, parentCardId = OtherId, clearParent = true, expectedUpdatedAt = Stamp })];
        yield return [Card("update", new { cardId = CardId, clearParent = "false", expectedUpdatedAt = Stamp })];
        yield return [Card("archive-lifecycle", new { cardId = CardId })];
        yield return [Card("restore-lifecycle", new { cardId = CardId, expectedUpdatedAt = 12 })];
        yield return [Card("delete", new { cardId = CardId, expectedUpdatedAt = Stamp, expectedChildrenFingerprint = 12 })];
        yield return [Card("move", new { cardId = CardId, columnId = ColumnId, clearParent = false })];
        yield return [Card("move", new { cardId = CardId, columnId = ColumnId, estimatedEffortMinutes = (int?)null })];
        yield return [Card("create", new { boardId = BoardId, columnId = ColumnId, title = 12 })];
        yield return [Card("create", new { boardId = BoardId, columnId = ColumnId, title = "Card", clearParent = true })];
        yield return [Card("create", new { boardId = BoardId, columnId = ColumnId, title = "Card", clearEstimatedEffort = false })];
        yield return [Operation("reorder", "column", new { columnId = ColumnId }, ColumnId)];
        yield return [Operation("reorder", "column", new { columnId = ColumnId, position = -1 }, ColumnId)];
        yield return [Operation("reorder", "column", new { columnId = OtherId, position = 0 }, ColumnId)];
        yield return [Operation("create", "column", new { boardId = BoardId, name = "New", position = 0, ignored = true }, null)];
        yield return [Operation("create", "column", new { boardId = BoardId, name = "New", position = 0 }, ColumnId)];
        yield return [Card("update", new { cardId = CardId, title = new string('x', 201) })];
        yield return [Card("update", new { cardId = CardId, title = "Valid" }) with { Parameters = "[]" }];
        yield return [Card("future-action", new { cardId = CardId })];
    }

    [Theory]
    [MemberData(nameof(MalformedOperations))]
    public async Task MalformedShape_FailsBothConsumersBeforeAnyRepositoryAccess(ProposalOperationDto operation)
    {
        var shape = ProposalOperationContractValidator.ValidateShape([operation], out var count);
        var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var full = await ProposalOperationContractValidator.ValidateAsync(unit.Object, BoardId, [operation]);

        shape.IsSuccess.Should().BeFalse();
        count.Should().Be(1);
        full.IsSuccess.Should().BeFalse();
        full.ErrorCode.Should().Be(shape.ErrorCode);
        full.ErrorMessage.Should().Be(shape.ErrorMessage);
        unit.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("two-relations")]
    [InlineData("relation-lifecycle")]
    [InlineData("assignment-update")]
    [InlineData("type-update")]
    [InlineData("two-hierarchy-operations")]
    public async Task InvalidOperationSet_IsCountedAndRejectedBeforeDatabaseReads(string scenario)
    {
        var first = scenario switch
        {
            "assignment-update" => Card("replace-assignments", new { cardId = CardId, userIds = Array.Empty<Guid>(), expectedUpdatedAt = Stamp }),
            "type-update" => Card("update", new { cardId = CardId, workItemType = "Epic", expectedUpdatedAt = Stamp }),
            "two-hierarchy-operations" => Card("archive-lifecycle", new { cardId = CardId, expectedUpdatedAt = Stamp }),
            _ => Relation("add-relation"),
        };
        var second = scenario switch
        {
            "two-relations" => Relation("remove-relation"),
            "relation-lifecycle" => Card("delete", new { cardId = CardId, expectedUpdatedAt = Stamp }),
            "two-hierarchy-operations" => Card("restore-lifecycle", new { cardId = OtherId, expectedUpdatedAt = Stamp }) with { TargetId = OtherId.ToString() },
            _ => Card("update", new { cardId = CardId, title = "Change" }),
        };
        ProposalOperationDto[] operations = [first, second with { Sequence = 1 }];
        var shape = ProposalOperationContractValidator.ValidateShape(operations, out var count);
        var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var full = await ProposalOperationContractValidator.ValidateAsync(unit.Object, BoardId, operations);

        shape.IsSuccess.Should().BeFalse();
        count.Should().Be(2);
        full.ErrorMessage.Should().Be(shape.ErrorMessage);
        unit.VerifyNoOtherCalls();
    }

    [Fact]
    public void CreateThenEstimateUpdate_DoesNotRequireAPreexistingCardTimestamp()
    {
        var create = Card("create", new { boardId = BoardId, columnId = ColumnId, title = "New card" });
        var update = Card("update", new { cardId = CardId, estimatedEffortMinutes = 15 }) with { Sequence = 1 };

        // Enumeration order is not execution order.
        ProposalOperationContractValidator.ValidateShape([update, create], out var count).IsSuccess.Should().BeTrue();
        count.Should().Be(0);
        ProposalOperationContractValidator.ValidateShape([update with { Sequence = 0 }, create with { Sequence = 1 }], out count)
            .IsSuccess.Should().BeFalse();
        count.Should().Be(1);
    }

    [Fact]
    public void ExistingCardEstimateChanges_RequireSyntacticPin_ButDoNotCompareItToCurrentState()
    {
        var first = Card("update", new { cardId = CardId, estimatedEffortMinutes = 10, expectedUpdatedAt = Stamp });
        var second = Card("update", new { cardId = CardId, estimatedEffortMinutes = 20, expectedUpdatedAt = Stamp }) with { Sequence = 1 };

        ProposalOperationContractValidator.ValidateShape([first, second], out var count).IsSuccess.Should().BeTrue();
        count.Should().Be(0);
    }

    [Fact]
    public void DistinctCardAssignmentAndUpdate_AreNotBlanketRejected()
    {
        var assignment = Card("replace-assignments", new { cardId = CardId, userIds = Array.Empty<Guid>(), expectedUpdatedAt = Stamp });
        var update = Card("update", new { cardId = OtherId, title = "Other card" }) with { Sequence = 1, TargetId = OtherId.ToString() };

        ProposalOperationContractValidator.ValidateShape([assignment, update], out var count).IsSuccess.Should().BeTrue();
        count.Should().Be(0);
    }

    [Theory]
    [InlineData("archive-lifecycle")]
    [InlineData("restore-lifecycle")]
    [InlineData("delete")]
    public void LifecycleShape_DoesNotNeedExistingActiveEntitiesOrACurrentTimestamp(string action)
    {
        var operation = Card(action, new { cardId = CardId, expectedUpdatedAt = Stamp });

        ProposalOperationContractValidator.ValidateShape([operation], out var count).IsSuccess.Should().BeTrue();
        count.Should().Be(0);
    }

    [Fact]
    public void ValidBoardAndColumnShapes_AreAcceptedWithoutRepositories()
    {
        var board = Operation("update", "board", new { boardId = BoardId, name = "Renamed" }, BoardId);
        var column = Operation("create", "column", new { boardId = BoardId, name = "New", position = 2, wipLimit = 5 }, null) with { Sequence = 1 };

        ProposalOperationContractValidator.ValidateShape([board, column], out var count).IsSuccess.Should().BeTrue();
        count.Should().Be(0);
    }

    [Fact]
    public void MalformedAndValidSiblings_CountOnlyMalformedOperations()
    {
        var malformed = Card("update", new { title = "Missing cardId" });
        var valid = Card("update", new { cardId = CardId, title = "Valid" }) with { Sequence = 1 };

        ProposalOperationContractValidator.ValidateShape([malformed, valid], out var count).IsSuccess.Should().BeFalse();
        count.Should().Be(1);
    }

    private static ProposalOperationDto Relation(string action) => Card(action,
        new { boardId = BoardId, cardId = CardId, relatedCardId = OtherId, relationType = "blocks", expectedRevision = 0 });

    private static ProposalOperationDto Card(string action, object parameters) => Operation(action, "card", parameters, CardId);

    private static ProposalOperationDto Operation(string action, string target, object parameters, Guid? targetId) => new(
        Guid.NewGuid(), Guid.NewGuid(), 0, action, target, targetId?.ToString(), JsonSerializer.Serialize(parameters), Guid.NewGuid().ToString(), null);
}
