using System.Text.Json;
using FluentAssertions;
using Moq;
using Xunit;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Tools;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Edge case tests for ToolExecutorRegistry.
/// Covers: empty registry, case-insensitive lookup, GetRegisteredToolNames,
/// duplicate tool names, and null/empty tool name lookup.
/// </summary>
public class ToolExecutorRegistryEdgeCaseTests
{
    private static Mock<IToolExecutor> MakeExecutor(string name)
    {
        var mock = new Mock<IToolExecutor>();
        mock.SetupGet(e => e.ToolName).Returns(name);
        mock.Setup(e => e.ExecuteAsync(It.IsAny<Guid>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{}");
        mock.Setup(e => e.ExecuteAsync(It.IsAny<ToolExecutionContext>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{}");
        return mock;
    }

    [Fact]
    public void GetExecutor_EmptyRegistry_ReturnsNull()
    {
        var registry = new ToolExecutorRegistry(Array.Empty<IToolExecutor>());

        var result = registry.GetExecutor("list_board_columns");

        result.Should().BeNull();
    }

    [Fact]
    public void GetExecutor_CaseInsensitive_FindsTool()
    {
        var executor = MakeExecutor("list_board_columns");
        var registry = new ToolExecutorRegistry(new[] { executor.Object });

        var result = registry.GetExecutor("LIST_BOARD_COLUMNS");

        result.Should().NotBeNull();
        result!.ToolName.Should().Be("list_board_columns");
    }

    [Fact]
    public void GetExecutor_MixedCase_FindsTool()
    {
        var executor = MakeExecutor("Get_Card_Details");
        var registry = new ToolExecutorRegistry(new[] { executor.Object });

        var result = registry.GetExecutor("get_card_details");

        result.Should().NotBeNull();
    }

    [Fact]
    public void GetRegisteredToolNames_EmptyRegistry_ReturnsEmpty()
    {
        var registry = new ToolExecutorRegistry(Array.Empty<IToolExecutor>());

        var names = registry.GetRegisteredToolNames();

        names.Should().BeEmpty();
    }

    [Fact]
    public void GetRegisteredToolNames_MultipleExecutors_ReturnsAll()
    {
        var e1 = MakeExecutor("list_board_columns");
        var e2 = MakeExecutor("get_card_details");
        var e3 = MakeExecutor("search_cards");
        var registry = new ToolExecutorRegistry(new[] { e1.Object, e2.Object, e3.Object });

        var names = registry.GetRegisteredToolNames();

        names.Should().HaveCount(3);
        names.Should().Contain("list_board_columns");
        names.Should().Contain("get_card_details");
        names.Should().Contain("search_cards");
    }

    [Fact]
    public void GetExecutor_NonExistentTool_ReturnsNull()
    {
        var executor = MakeExecutor("list_board_columns");
        var registry = new ToolExecutorRegistry(new[] { executor.Object });

        var result = registry.GetExecutor("nonexistent_tool");

        result.Should().BeNull();
    }

    [Fact]
    public void GetExecutor_EmptyString_ReturnsNull()
    {
        var executor = MakeExecutor("list_board_columns");
        var registry = new ToolExecutorRegistry(new[] { executor.Object });

        var result = registry.GetExecutor("");

        result.Should().BeNull();
    }

    [Fact]
    public void ToolExecutionContext_Properties_AreAccessible()
    {
        var boardId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var context = new ToolExecutionContext(boardId, userId);

        context.BoardId.Should().Be(boardId);
        context.UserId.Should().Be(userId);
    }

    [Fact]
    public void ToolExecutionContext_Equality_WorksByValue()
    {
        var boardId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var ctx1 = new ToolExecutionContext(boardId, userId);
        var ctx2 = new ToolExecutionContext(boardId, userId);

        ctx1.Should().Be(ctx2); // Record equality
    }

    [Fact]
    public void Constructor_DuplicateToolNames_ThrowsOnCreation()
    {
        // ToolExecutorRegistry uses ToDictionary internally which throws
        // ArgumentException on duplicate keys. This verifies the crash behavior
        // is surfaced rather than silently losing executors.
        var e1 = MakeExecutor("list_board_columns");
        var e2 = MakeExecutor("list_board_columns"); // duplicate

        var act = () => new ToolExecutorRegistry(new[] { e1.Object, e2.Object });

        act.Should().Throw<ArgumentException>("duplicate tool names should not be silently accepted");
    }

    [Fact]
    public void GetExecutor_NullToolName_ThrowsArgumentNullException()
    {
        var executor = MakeExecutor("list_board_columns");
        var registry = new ToolExecutorRegistry(new[] { executor.Object });

        // Dictionary.TryGetValue throws ArgumentNullException on null key
        var act = () => registry.GetExecutor(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
