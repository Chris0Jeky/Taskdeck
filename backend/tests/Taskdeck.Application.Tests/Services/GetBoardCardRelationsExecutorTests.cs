using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Tools;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public sealed class GetBoardCardRelationsExecutorTests
{
    [Fact]
    public async Task ExecutesAgainstTrustedContextAndReturnsExactGraph()
    {
        var relations = new Mock<IBoardRelationService>();
        var boardId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var edge = new CardRelationEdge(Guid.NewGuid(), Guid.NewGuid(), "blocks");
        relations.Setup(service => service.GetAsync(userId, boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new BoardRelationsDto(boardId, 42, [edge], true)));
        var executor = new GetBoardCardRelationsExecutor(relations.Object);
        using var args = JsonDocument.Parse("{}");

        var result = await executor.ExecuteAsync(new ToolExecutionContext(boardId, userId), args.RootElement);

        using var document = JsonDocument.Parse(result);
        document.RootElement.GetProperty("revision").GetInt64().Should().Be(42);
        var returnedEdge = document.RootElement.GetProperty("relations").EnumerateArray().Should().ContainSingle().Subject;
        returnedEdge.GetProperty("source_card_id").GetGuid().Should().Be(edge.SourceCardId);
        returnedEdge.GetProperty("target_card_id").GetGuid().Should().Be(edge.TargetCardId);
        returnedEdge.GetProperty("relation_type").GetString().Should().Be(edge.RelationType);
        relations.Verify(service => service.GetAsync(userId, boardId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeniedReadReturnsErrorWithoutChangingTheGraph()
    {
        var relations = new Mock<IBoardRelationService>();
        relations.Setup(service => service.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<BoardRelationsDto>(ErrorCodes.Forbidden, "No board access."));
        var executor = new GetBoardCardRelationsExecutor(relations.Object);
        using var args = JsonDocument.Parse("{}");

        var result = await executor.ExecuteAsync(new ToolExecutionContext(Guid.NewGuid(), Guid.NewGuid()), args.RootElement);

        JsonDocument.Parse(result).RootElement.GetProperty("error").GetString().Should().Contain("No board access");
    }
}
