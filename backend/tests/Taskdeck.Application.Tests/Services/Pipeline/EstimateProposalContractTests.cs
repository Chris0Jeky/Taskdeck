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

public class EstimateProposalContractTests
{
    [Theory]
    [InlineData("{}", null)]
    [InlineData("{\"estimatedEffortMinutes\":null}", null)]
    [InlineData("{\"estimatedEffortMinutes\":0}", 0)]
    [InlineData("{\"estimatedEffortMinutes\":90}", 90)]
    [InlineData("{\"estimatedEffortMinutes\":1000000}", 1000000)]
    public void Parser_PreservesUnknownAndWholeMinutes(string json, int? expected)
    {
        using var document = JsonDocument.Parse(json);
        OperationParameterParser.TryGetEstimatedEffortMinutes(document.RootElement, out var minutes, out var error).Should().BeTrue(error);
        minutes.Should().Be(expected);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("1.5")]
    [InlineData("1.0")]
    [InlineData("-1")]
    [InlineData("1000001")]
    [InlineData("2147483648")]
    [InlineData("\"90\"")]
    [InlineData("\"-1\"")]
    [InlineData("\"1.5\"")]
    [InlineData("\"1000001\"")]
    [InlineData("[]")]
    [InlineData("{}")]
    public async Task MalformedEstimate_IsRejectedForCreateAndUpdateWithoutWrites(string raw)
    {
        var (unit, board, column, card) = Fixture();
        foreach (var action in new[] { "create", "update" })
        {
            var parameters = Parameters(board, column, card, raw);
            var result = await ProposalOperationContractValidator.ValidateAsync(unit.Object, board.Id, [Operation(action, card, parameters)]);
            result.ErrorCode.Should().Be(ErrorCodes.ValidationError, raw);
        }
        card.EstimatedEffortMinutes.Should().BeNull();
        unit.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("90")]
    [InlineData("1000000")]
    public async Task CreateAndUpdate_AcceptBoundedNullableEstimate(string raw)
    {
        var (unit, board, column, card) = Fixture();
        foreach (var action in new[] { "create", "update" })
        {
            var result = await ProposalOperationContractValidator.ValidateAsync(unit.Object, board.Id,
                [Operation(action, card, Parameters(board, column, card, raw))]);
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        }
    }

    [Theory]
    [InlineData("\"clearEstimatedEffort\":true", true)]
    [InlineData("\"estimatedEffortMinutes\":null,\"clearEstimatedEffort\":true", true)]
    [InlineData("\"estimatedEffortMinutes\":0,\"clearEstimatedEffort\":true", false)]
    [InlineData("\"estimatedEffortMinutes\":30,\"clearEstimatedEffort\":true", false)]
    [InlineData("\"estimatedEffortMinutes\":30,\"clearEstimatedEffort\":false", true)]
    [InlineData("\"clearEstimatedEffort\":\"true\"", false)]
    [InlineData("\"clearEstimatedEffort\":null", false)]
    public async Task ClearContract_IsExplicitAndUpdateOnly(string fields, bool valid)
    {
        var (unit, board, column, card) = Fixture();
        var parameters = Parameters(board, column, card, null).TrimEnd('}') + "," + fields + "}";
        var result = await ProposalOperationContractValidator.ValidateAsync(unit.Object, board.Id, [Operation("update", card, parameters)]);
        result.IsSuccess.Should().Be(valid, result.ErrorMessage);
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, board.Id, [Operation("create", card, parameters)]))
            .ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Update_RequiresInitialTimestampOnlyForIntentionalEstimateChanges()
    {
        var (unit, board, _, card) = Fixture();
        foreach (var fields in new[] { "\"estimatedEffortMinutes\":0", "\"clearEstimatedEffort\":true" })
        {
            var parameters = $"{{\"cardId\":\"{card.Id}\",{fields}}}";
            (await ProposalOperationContractValidator.ValidateAsync(unit.Object, board.Id, [Operation("update", card, parameters)]))
                .ErrorCode.Should().Be(ErrorCodes.ValidationError);
            var stale = parameters.TrimEnd('}') + $",\"expectedUpdatedAt\":\"{card.UpdatedAt.AddMinutes(-1):O}\"}}";
            (await ProposalOperationContractValidator.ValidateAsync(unit.Object, board.Id, [Operation("update", card, stale)]))
                .ErrorCode.Should().Be(ErrorCodes.Conflict);
        }
        var omission = JsonSerializer.Serialize(new { cardId = card.Id, title = "Rename only", estimatedEffortMinutes = (int?)null, clearEstimatedEffort = false });
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, board.Id, [Operation("update", card, omission)]))
            .IsSuccess.Should().BeTrue();
        var noChange = JsonSerializer.Serialize(new { cardId = card.Id, estimatedEffortMinutes = (int?)null });
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, board.Id, [Operation("update", card, noChange)]))
            .ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData("move")]
    [InlineData("archive")]
    [InlineData("add-label")]
    public async Task Estimate_IsRejectedWhenActionDoesNotApplyIt(string action)
    {
        var (unit, board, column, card) = Fixture();
        (await ProposalOperationContractValidator.ValidateAsync(unit.Object, board.Id,
            [Operation(action, card, Parameters(board, column, card, "30"))])).ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData("B")]
    [InlineData("N")]
    public async Task EstimatePin_UsesTheSameGuidFormatsAsOperationScope(string format)
    {
        var (unit, board, _, card) = Fixture();
        var parameters = JsonSerializer.Serialize(new { cardId = card.Id.ToString(format), estimatedEffortMinutes = 0, expectedUpdatedAt = card.UpdatedAt });
        var result = await ProposalOperationContractValidator.ValidateAsync(unit.Object, board.Id, [Operation("update", card, parameters)]);
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    private static string Parameters(Board board, Column column, Card card, string? estimate) =>
        JsonSerializer.Serialize(new { boardId = board.Id, columnId = column.Id, cardId = card.Id, title = "Estimate", expectedUpdatedAt = card.UpdatedAt })
            .TrimEnd('}') + (estimate is null ? "}" : $",\"estimatedEffortMinutes\":{estimate}}}");

    private static ProposalOperationDto Operation(string action, Card card, string parameters) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 0, action, "card", action == "create" ? null : card.Id.ToString(), parameters, Guid.NewGuid().ToString(), null);

    private static (Mock<IUnitOfWork> Unit, Board Board, Column Column, Card Card) Fixture()
    {
        var board = new Board("Estimates");
        var column = new Column(board.Id, "Now", 0);
        var card = new Card(board.Id, column.Id, "Existing");
        var unit = new Mock<IUnitOfWork>();
        var cards = new Mock<ICardRepository>();
        var columns = new Mock<IColumnRepository>();
        var boards = new Mock<IBoardRepository>();
        unit.SetupGet(u => u.Cards).Returns(cards.Object);
        unit.SetupGet(u => u.Columns).Returns(columns.Object);
        unit.SetupGet(u => u.Boards).Returns(boards.Object);
        cards.Setup(r => r.GetByIdAsync(card.Id, It.IsAny<CancellationToken>())).ReturnsAsync(card);
        columns.Setup(r => r.GetByIdAsync(column.Id, It.IsAny<CancellationToken>())).ReturnsAsync(column);
        columns.Setup(r => r.GetByIdWithCardsAsync(column.Id, It.IsAny<CancellationToken>())).ReturnsAsync(column);
        boards.Setup(r => r.GetByIdAsync(board.Id, It.IsAny<CancellationToken>())).ReturnsAsync(board);
        return (unit, board, column, card);
    }
}
