using FluentAssertions;
using Moq;
using Taskdeck.Api.Mcp;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

public class BoardResourcesErrorSafetyTests
{
    private const string HostileError =
        "Bearer tdsk_test_secret C:\\Users\\alice\\taskdeck.db " +
        "SQLite UNIQUE constraint failed: Boards.Name https://provider.example/v1/internal";

    [Fact]
    public async Task ListBoards_UnexpectedAuthorizationFailure_UsesGenericMessage()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var (resources, unitOfWork, authorization) = CreateResources(userId);
        var boardRepository = new Mock<IBoardRepository>(MockBehavior.Strict);
        unitOfWork
            .SetupGet(value => value.Boards)
            .Returns(boardRepository.Object);
        boardRepository
            .Setup(repository => repository.SearchIdsAsync(null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { boardId });
        authorization
            .Setup(service => service.GetReadableBoardIdsAsync(
                userId,
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlySet<Guid>>(ErrorCodes.UnexpectedError, HostileError));

        var action = () => resources.ListBoards();

        var exception = (await action.Should().ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be(
            $"MCP: failed to list boards: {SensitiveDataRedactor.GenericUnexpectedFailureMessage}");
        exception.Message.Should().NotContain(HostileError);
        authorization.VerifyAll();
        boardRepository.VerifyAll();
    }

    [Theory]
    [InlineData("detail")]
    [InlineData("column")]
    [InlineData("card")]
    [InlineData("labels")]
    public async Task BoardAccess_UnexpectedFailure_UsesGenericMessage(string resource)
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var (resources, _, authorization) = CreateResources(userId);
        authorization
            .Setup(service => service.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Failure<bool>(ErrorCodes.UnexpectedError, HostileError));

        Func<Task<string>> action = resource switch
        {
            "detail" => () => resources.GetBoardDetail(boardId.ToString()),
            "column" => () => resources.GetColumnCards(boardId.ToString(), Guid.NewGuid().ToString()),
            "card" => () => resources.GetCardDetail(boardId.ToString(), Guid.NewGuid().ToString()),
            "labels" => () => resources.GetBoardLabels(boardId.ToString()),
            _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, null)
        };

        var exception = (await action.Should().ThrowAsync<InvalidOperationException>()).Which;
        var prefix = resource switch
        {
            "detail" => "MCP: failed to get board detail: ",
            "column" => "MCP: failed to access board: ",
            "card" => "MCP: failed to access board: ",
            "labels" => "MCP: failed to access board: ",
            _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, null)
        };
        exception.Message.Should().Be(prefix + SensitiveDataRedactor.GenericUnexpectedFailureMessage);
        authorization.VerifyAll();
    }

    [Fact]
    public async Task GetBoardDetail_KnownDomainFailure_PreservesStableMessage()
    {
        const string stableMessage = "You do not have access to this board.";
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var (resources, _, authorization) = CreateResources(userId);
        authorization
            .Setup(service => service.CanReadBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Failure<bool>(ErrorCodes.Forbidden, stableMessage));

        var action = () => resources.GetBoardDetail(boardId.ToString());

        var exception = (await action.Should().ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Be($"MCP: failed to get board detail: {stableMessage}");
        authorization.VerifyAll();
    }

    private static (
        BoardResources Resources,
        Mock<IUnitOfWork> UnitOfWork,
        Mock<IAuthorizationService> Authorization) CreateResources(Guid userId)
    {
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
        var boardService = new BoardService(unitOfWork.Object, authorization.Object);
        var resources = new BoardResources(
            boardService,
            new ColumnService(unitOfWork.Object),
            new CardService(unitOfWork.Object),
            new LabelService(unitOfWork.Object),
            new FixedUserContextProvider(userId));

        return (resources, unitOfWork, authorization);
    }

    private sealed class FixedUserContextProvider(Guid userId) : IUserContextProvider
    {
        public Task<McpUserContext> GetCurrentContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new McpUserContext(userId, ApiKeyScope.Full));

        public Task<Guid> GetCurrentUserIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(userId);

        public Task<Guid?> GetUserIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(userId);
    }
}
