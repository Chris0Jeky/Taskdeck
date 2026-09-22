using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Api.Mcp;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class WriteToolsBoardVisibilityTests
{
    [Theory]
    [InlineData("move", false)]
    [InlineData("move", true)]
    [InlineData("update", false)]
    [InlineData("update", true)]
    [InlineData("update-type", false)]
    [InlineData("update-type", true)]
    [InlineData("archive", false)]
    [InlineData("archive", true)]
    public async Task MissingAndInaccessibleBoards_ReturnIdenticalErrorsWithoutProposing(
        string tool, bool viewer)
    {
        var caller = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var board = new Board("Private board must stay hidden", ownerId: owner);
        var missing = Guid.NewGuid();
        var boards = new Mock<IBoardRepository>(MockBehavior.Strict);
        boards.Setup(repository => repository.GetByIdAsync(board.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);
        boards.Setup(repository => repository.GetByIdAsync(missing, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Board?)null);
        var accesses = new Mock<IBoardAccessRepository>(MockBehavior.Strict);
        accesses.Setup(repository => repository.GetByBoardAndUserAsync(
                board.Id, caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(viewer ? new BoardAccess(board.Id, caller, UserRole.Viewer, owner) : null);
        var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unit.SetupGet(work => work.Boards).Returns(boards.Object);
        unit.SetupGet(work => work.BoardAccesses).Returns(accesses.Object);
        var proposals = new Mock<IAutomationProposalService>(MockBehavior.Strict);
        var tools = CreateTools(caller, unit.Object, proposals.Object, new AuthorizationService(unit.Object));

        var inaccessibleResponse = await InvokeAsync(tools, tool, board.Id);
        var missingResponse = await InvokeAsync(tools, tool, missing);

        missingResponse.Should().Be(inaccessibleResponse,
            "the tool response must not reveal whether an inaccessible board exists");
        ReadError(missingResponse).Should().Be(DenialMessage(tool));
        missingResponse.Should().NotContain(board.Name).And.NotContain(board.Id.ToString())
            .And.NotContain(missing.ToString());
        proposals.VerifyNoOtherCalls();
        unit.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        unit.VerifyGet(work => work.Cards, Times.Never);
    }

    [Theory]
    [InlineData("move")]
    [InlineData("update")]
    [InlineData("archive")]
    public async Task ExplicitForbiddenResult_UsesTheSameDenialAsFalse(string tool)
    {
        var caller = Guid.NewGuid();
        var board = Guid.NewGuid();
        var authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
        authorization.SetupSequence(service => service.CanWriteBoardAsync(caller, board))
            .ReturnsAsync(Result.Success(false))
            .ReturnsAsync(Result.Failure<bool>(ErrorCodes.Forbidden, "Private membership details"));
        var proposals = new Mock<IAutomationProposalService>(MockBehavior.Strict);
        var tools = CreateTools(caller, new Mock<IUnitOfWork>(MockBehavior.Strict).Object,
            proposals.Object, authorization.Object);

        var denied = await InvokeAsync(tools, tool, board);
        var forbidden = await InvokeAsync(tools, tool, board);

        forbidden.Should().Be(denied);
        ReadError(forbidden).Should().Be(DenialMessage(tool));
        proposals.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("move")]
    [InlineData("update")]
    [InlineData("archive")]
    public async Task UnexpectedAuthorizationFailure_RemainsSanitizedAndDoesNotPropose(string tool)
    {
        var caller = Guid.NewGuid();
        var board = Guid.NewGuid();
        var authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
        authorization.Setup(service => service.CanWriteBoardAsync(caller, board))
            .ReturnsAsync(Result.Failure<bool>(ErrorCodes.UnexpectedError, "private-db-path-and-secret"));
        var proposals = new Mock<IAutomationProposalService>(MockBehavior.Strict);
        var tools = CreateTools(caller, new Mock<IUnitOfWork>(MockBehavior.Strict).Object,
            proposals.Object, authorization.Object);

        var response = await InvokeAsync(tools, tool, board);

        ReadError(response).Should().Be(SensitiveDataRedactor.GenericUnexpectedFailureMessage)
            .And.NotBe(DenialMessage(tool));
        response.Should().NotContain("private-db-path-and-secret");
        proposals.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("move")]
    [InlineData("update")]
    [InlineData("archive")]
    public async Task WritableBoard_ReachesProposalCreationWithTheAuthenticatedActor(string tool)
    {
        var caller = Guid.NewGuid();
        var board = Guid.NewGuid();
        var authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
        authorization.Setup(service => service.CanWriteBoardAsync(caller, board))
            .ReturnsAsync(Result.Success(true));
        var proposals = new Mock<IAutomationProposalService>(MockBehavior.Strict);
        proposals.Setup(service => service.CreateProposalAsync(
                It.Is<CreateProposalDto>(dto => dto.BoardId == board && dto.RequestedByUserId == caller),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProposalDto>(ErrorCodes.ValidationError, "Proposal service reached"));
        var tools = CreateTools(caller, new Mock<IUnitOfWork>(MockBehavior.Strict).Object,
            proposals.Object, authorization.Object);

        var response = await InvokeAsync(tools, tool, board);

        ReadError(response).Should().Be("Proposal service reached");
        proposals.Verify(service => service.CreateProposalAsync(
            It.Is<CreateProposalDto>(dto => dto.BoardId == board && dto.RequestedByUserId == caller),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Task<string> InvokeAsync(WriteTools tools, string tool, Guid board)
    {
        var card = Guid.NewGuid().ToString();
        return tool switch
        {
            "move" => tools.MoveCard(board.ToString(), card, Guid.NewGuid().ToString()),
            "update" => tools.UpdateCard(board.ToString(), card, title: "Proposed title"),
            "update-type" => tools.UpdateCard(board.ToString(), card, work_item_type: "Task",
                expected_updated_at: "2026-09-22T00:00:00Z"),
            "archive" => tools.ArchiveCard(board.ToString(), card),
            _ => throw new ArgumentOutOfRangeException(nameof(tool))
        };
    }

    private static string DenialMessage(string tool) =>
        $"Not authorized to {(tool == "update-type" ? "update" : tool)} cards on this board";

    private static string? ReadError(string response)
    {
        using var document = JsonDocument.Parse(response);
        return document.RootElement.GetProperty("error").GetString();
    }

    private static WriteTools CreateTools(Guid caller, IUnitOfWork unit,
        IAutomationProposalService proposals, IAuthorizationService authorization) =>
        new(proposals, new McpBoardResourcesTests.FixedUserContextProvider(caller),
            new Mock<ICaptureService>(MockBehavior.Strict).Object, unit, authorization);
}
