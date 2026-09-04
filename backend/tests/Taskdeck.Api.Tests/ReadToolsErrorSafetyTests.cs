using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Api.Mcp;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ReadToolsErrorSafetyTests
{
    private const string HostileError =
        "Bearer tdsk_test_secret C:\\Users\\alice\\taskdeck.db " +
        "SQLite UNIQUE constraint failed: Boards.Name https://provider.example/v1/internal";

    private static readonly string[] HostileMarkers =
    [
        "tdsk_test_secret",
        "C:\\Users\\alice\\taskdeck.db",
        "UNIQUE constraint failed",
        "https://provider.example/v1/internal"
    ];

    [Theory]
    [InlineData("search")]
    [InlineData("summary")]
    public async Task BoardDetail_UnexpectedFailure_ReturnsGenericError(string operation)
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
        authorization
            .Setup(service => service.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Failure<bool>(ErrorCodes.UnexpectedError, HostileError));
        var tools = CreateTools(userId, unitOfWork, authorization);

        var json = operation switch
        {
            "search" => await tools.SearchCards("needle", boardId.ToString()),
            "summary" => await tools.GetBoardSummary(boardId.ToString()),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

        AssertGenericError(json);
        authorization.VerifyAll();
    }

    [Fact]
    public async Task SearchCards_UnexpectedBoardListFailure_ReturnsGenericError()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var boards = new Mock<IBoardRepository>(MockBehavior.Strict);
        var authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
        unitOfWork.SetupGet(value => value.Boards).Returns(boards.Object);
        boards
            .Setup(repository => repository.SearchIdsAsync(
                null,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { boardId });
        authorization
            .Setup(service => service.GetReadableBoardIdsAsync(
                userId,
                It.Is<IEnumerable<Guid>>(ids => ids.SequenceEqual(new[] { boardId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlySet<Guid>>(
                ErrorCodes.UnexpectedError,
                HostileError));
        var tools = CreateTools(userId, unitOfWork, authorization);

        var json = await tools.SearchCards("needle");

        AssertGenericError(json);
        authorization.VerifyAll();
        boards.VerifyAll();
    }

    [Fact]
    public async Task GetBoardSummary_KnownDomainFailure_PreservesStableMessage()
    {
        const string stableMessage = "You do not have access to this board.";
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
        authorization
            .Setup(service => service.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Failure<bool>(ErrorCodes.Forbidden, stableMessage));
        var tools = CreateTools(userId, unitOfWork, authorization);

        var json = await tools.GetBoardSummary(boardId.ToString());

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("error").GetString().Should().Be(stableMessage);
        authorization.VerifyAll();
    }

    private static ReadTools CreateTools(
        Guid userId,
        Mock<IUnitOfWork> unitOfWork,
        Mock<IAuthorizationService> authorization)
    {
        return new ReadTools(
            new BoardService(unitOfWork.Object, authorization.Object),
            new CardService(unitOfWork.Object),
            new McpBoardResourcesTests.FixedUserContextProvider(userId));
    }

    private static void AssertGenericError(string json)
    {
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("error").GetString()
            .Should().Be(SensitiveDataRedactor.GenericUnexpectedFailureMessage);

        foreach (var marker in HostileMarkers)
            json.Should().NotContain(marker);
    }
}
