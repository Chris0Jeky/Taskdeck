using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services.Pipeline;

public class ProposalIgnoredCardParametersTests
{
    public static IEnumerable<object[]> UnsupportedFields()
    {
        string[] actions = ["move", "delete", "archive", "archive-lifecycle", "restore-lifecycle",
            "replace-assignments", "add-label", "remove-label", "add-relation", "remove-relation"];
        string[] fields = ["title", "description", "dueDate", "clearDueDate", "labels", "labelIds",
            "workItemType", "parentCardId", "clearParent"];
        foreach (var action in actions)
            foreach (var field in fields)
                yield return [action, field];
    }

    [Theory]
    [MemberData(nameof(UnsupportedFields))]
    public async Task IgnoredFields_RejectBeforeHierarchyOrRepositoryReads(string action, string field)
    {
        var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var parameters = new Dictionary<string, object?> { [field] = ValueFor(field) };
        var operation = Operation(action, parameters);

        var result = await ProposalOperationContractValidator.ValidateAsync(unit.Object, Guid.NewGuid(), [operation]);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Be($"Parameter '{field}' is not supported by card action '{action}'");
        unit.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("move", "dueDate", "null")]
    [InlineData("delete", "labels", "null")]
    [InlineData("move", "clearDueDate", "false")]
    [InlineData("move", "clearParent", "false")]
    [InlineData("move", "parentCardId", "null")]
    [InlineData("MOVE", "dueDate", "\"2027-01-01\"")]
    [InlineData("add_label", "labels", "[]")]
    [InlineData("removelabel", "labelIds", "[]")]
    public async Task UnsupportedParameterPresence_IsNotHiddenByNullFalseOrActionAliases(string action, string field, string json)
    {
        var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var operation = Operation(action, new Dictionary<string, object?> { [field] = JsonSerializer.Deserialize<JsonElement>(json) })
            with { TargetType = "CARD" };

        var result = await ProposalOperationContractValidator.ValidateAsync(unit.Object, Guid.NewGuid(), [operation]);

        result.ErrorMessage.Should().Be($"Parameter '{field}' is not supported by card action '{action}'");
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        unit.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("create", "title")]
    [InlineData("create", "description")]
    [InlineData("create", "dueDate")]
    [InlineData("create", "labels")]
    [InlineData("create", "labelIds")]
    [InlineData("create", "workItemType")]
    [InlineData("create", "parentCardId")]
    [InlineData("UPDATE", "title")]
    [InlineData("update", "description")]
    [InlineData("update", "dueDate")]
    [InlineData("update", "clearDueDate")]
    [InlineData("update", "labels")]
    [InlineData("update", "labelIds")]
    [InlineData("update", "workItemType")]
    [InlineData("update", "parentCardId")]
    [InlineData("update", "clearParent")]
    public async Task CreateAndUpdate_KeepTheirExistingFieldContracts(string action, string field)
    {
        var fixture = new Fixture();
        var create = action == "create";
        var parameters = fixture.Parameters(create);
        parameters[field] = field switch
        {
            "parentCardId" => fixture.Parent.Id,
            "clearDueDate" or "clearParent" => true,
            _ => ValueFor(field)
        };
        var operation = Operation(action, parameters) with { TargetId = create ? null : fixture.Card.Id.ToString() };

        var result = await ProposalOperationContractValidator.ValidateAsync(fixture.Unit.Object, fixture.Board.Id, [operation]);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData("add-label")]
    [InlineData("add_label")]
    [InlineData("addlabel")]
    [InlineData("remove-label")]
    [InlineData("remove_label")]
    [InlineData("removelabel")]
    public async Task SingularLabelParameters_KeepTheirDedicatedVerbAndAliasContracts(string action)
    {
        var fixture = new Fixture();
        var operation = Operation(action, new { cardId = fixture.Card.Id, labelId = fixture.Label.Id });

        var result = await ProposalOperationContractValidator.ValidateAsync(fixture.Unit.Object, fixture.Board.Id, [operation]);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        fixture.AssertNoWrites();
    }

    [Fact]
    public async Task NonCardDescription_IsNotMistakenForACardOnlyParameter()
    {
        var fixture = new Fixture();
        var operation = Operation("update", new { boardId = fixture.Board.Id, description = "Board description" })
            with { TargetType = "board", TargetId = fixture.Board.Id.ToString() };
        var result = await ProposalOperationContractValidator.ValidateAsync(fixture.Unit.Object, fixture.Board.Id, [operation]);
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        fixture.AssertNoWrites();
    }

    [Fact]
    public async Task ValidMove_StillRequiresItsBoardScope()
    {
        var fixture = new Fixture();
        var operation = Operation("move", new { cardId = fixture.Card.Id, columnId = fixture.Column.Id });
        var valid = await ProposalOperationContractValidator.ValidateAsync(fixture.Unit.Object, fixture.Board.Id, [operation]);
        valid.IsSuccess.Should().BeTrue(valid.ErrorMessage);
        var foreign = await ProposalOperationContractValidator.ValidateAsync(fixture.Unit.Object, Guid.NewGuid(), [operation]);
        foreign.IsSuccess.Should().BeFalse();
        foreign.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        fixture.AssertNoWrites();
    }

    [Theory]
    [InlineData("move")]
    [InlineData("delete")]
    public void Renderer_WithoutContractValidation_DoesNotDescribeIgnoredDateOrLabelEffects(string action)
    {
        var description = Render(action, new { dueDate = "2027-01-01", labels = new[] { "Priority" } });
        description.Should().NotContain("set due date").And.NotContain("replace labels");
        var clearDescription = Render(action, new { clearDueDate = true, labelIds = Array.Empty<Guid>() });
        clearDescription.Should().NotContain("clear due date").And.NotContain("replace labels");
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    public void Renderer_StillDescribesFieldsConsumedByCreateAndUpdate(string action)
    {
        var description = Render(action, new { dueDate = "2027-01-01", labels = new[] { "Priority" } });
        description.Should().Contain("set due date").And.Contain("2027-01-01").And.Contain("replace labels");
    }

    private static string Render(string action, object parameters)
    {
        // Deliberately bypass validation to test the defense-in-depth rendering branch,
        // not the public preview's rejection. The API tests exercise the real boundary.
        var service = typeof(AutomationProposalService);
        var viewType = service.GetNestedType("DiffOperationView", BindingFlags.NonPublic)!;
        var stateType = service.GetNestedType("CardDiffState", BindingFlags.NonPublic)!;
        var view = Activator.CreateInstance(viewType, 0, action, "card", null, JsonSerializer.Serialize(parameters));
        var states = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(Guid), stateType));
        var method = service.GetMethod("DescribeOperationReadable", BindingFlags.NonPublic | BindingFlags.Static)!;
        var names = new Dictionary<Guid, string>();
        return (string)method.Invoke(null, [view, names, names, states, names])!;
    }

    private static object? ValueFor(string field) => field switch
    {
        "title" => "Card title",
        "description" => "Description",
        "dueDate" => "2027-01-01",
        "clearDueDate" or "clearParent" => false,
        "labels" or "labelIds" => Array.Empty<string>(),
        "workItemType" => "Epic",
        "parentCardId" => null,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static ProposalOperationDto Operation(string action, object parameters) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 0, action, "card", null,
            JsonSerializer.Serialize(parameters), Guid.NewGuid().ToString(), null);

    private sealed class Fixture
    {
        public Mock<IUnitOfWork> Unit { get; } = new();
        public Board Board { get; } = new("Parameter board");
        public Column Column { get; }
        public Card Card { get; }
        public Card Parent { get; }
        public Label Label { get; }

        public Fixture()
        {
            Column = new Column(Board.Id, "Next", 0);
            Card = new Card(Board.Id, Column.Id, "Existing");
            Parent = new Card(Board.Id, Column.Id, "Parent");
            Label = new Label(Board.Id, "Priority", "#112233");
            var cards = new Mock<ICardRepository>();
            cards.Setup(repo => repo.GetByIdAsync(Card.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Card);
            cards.Setup(repo => repo.GetHierarchyByBoardIdAsync(Board.Id, It.IsAny<CancellationToken>())).ReturnsAsync([Card, Parent]);
            var columns = new Mock<IColumnRepository>();
            columns.Setup(repo => repo.GetByIdAsync(Column.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Column);
            var boards = new Mock<IBoardRepository>();
            boards.Setup(repo => repo.GetByIdAsync(Board.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Board);
            var labels = new Mock<ILabelRepository>();
            labels.Setup(repo => repo.GetByBoardIdAsync(Board.Id, It.IsAny<CancellationToken>())).ReturnsAsync([Label]);
            Unit.Setup(unit => unit.Cards).Returns(cards.Object);
            Unit.Setup(unit => unit.Columns).Returns(columns.Object);
            Unit.Setup(unit => unit.Boards).Returns(boards.Object);
            Unit.Setup(unit => unit.Labels).Returns(labels.Object);
        }

        public Dictionary<string, object?> Parameters(bool create) => create
            ? new() { ["boardId"] = Board.Id, ["columnId"] = Column.Id, ["title"] = "New card" }
            : new() { ["cardId"] = Card.Id, ["expectedUpdatedAt"] = Card.UpdatedAt };

        public void AssertNoWrites()
        {
            Unit.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            Unit.Verify(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
