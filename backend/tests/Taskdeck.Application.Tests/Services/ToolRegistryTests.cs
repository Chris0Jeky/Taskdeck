using FluentAssertions;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Agents;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ToolRegistryTests
{
    private readonly TaskdeckToolRegistry _registry = new();

    private static ITaskdeckTool CreateTool(
        string key = "test.tool",
        string displayName = "Test Tool",
        string description = "A test tool",
        ToolScope scope = ToolScope.Board,
        ToolRiskLevel riskLevel = ToolRiskLevel.Low)
    {
        return new TaskdeckToolDefinition(key, displayName, description, scope, riskLevel);
    }

    [Fact]
    public void RegisterTool_ShouldSucceed_ForNewKey()
    {
        var tool = CreateTool();

        var act = () => _registry.RegisterTool(tool);

        act.Should().NotThrow();
    }

    [Fact]
    public void RegisterTool_ShouldThrow_ForDuplicateKey()
    {
        _registry.RegisterTool(CreateTool("dup.key"));

        var act = () => _registry.RegisterTool(CreateTool("dup.key"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*dup.key*already registered*");
    }

    [Fact]
    public void RegisterTool_ShouldThrow_ForNullTool()
    {
        var act = () => _registry.RegisterTool(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterTool_ShouldThrow_ForEmptyKey()
    {
        var tool = CreateTool(key: "");

        var act = () => _registry.RegisterTool(tool);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetTool_ShouldReturnRegisteredTool()
    {
        var tool = CreateTool("board.create-card");
        _registry.RegisterTool(tool);

        var result = _registry.GetTool("board.create-card");

        result.Should().NotBeNull();
        result!.Key.Should().Be("board.create-card");
    }

    [Fact]
    public void GetTool_ShouldReturnNull_ForUnknownKey()
    {
        var result = _registry.GetTool("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public void GetTool_ShouldReturnNull_ForNullOrEmptyKey()
    {
        _registry.GetTool(null!).Should().BeNull();
        _registry.GetTool("").Should().BeNull();
        _registry.GetTool("  ").Should().BeNull();
    }

    [Fact]
    public void GetTool_ShouldBeCaseInsensitive()
    {
        _registry.RegisterTool(CreateTool("Board.CreateCard"));

        _registry.GetTool("board.createcard").Should().NotBeNull();
        _registry.GetTool("BOARD.CREATECARD").Should().NotBeNull();
    }

    [Fact]
    public void GetAllTools_ShouldReturnEmpty_WhenNoToolsRegistered()
    {
        _registry.GetAllTools().Should().BeEmpty();
    }

    [Fact]
    public void GetAllTools_ShouldReturnAllRegisteredTools()
    {
        _registry.RegisterTool(CreateTool("b.tool"));
        _registry.RegisterTool(CreateTool("a.tool"));
        _registry.RegisterTool(CreateTool("c.tool"));

        var all = _registry.GetAllTools();

        all.Should().HaveCount(3);
        all.Select(t => t.Key).Should().BeInAscendingOrder();
    }

    [Fact]
    public void GetToolsByScope_ShouldFilterByScope()
    {
        _registry.RegisterTool(CreateTool("board.one", scope: ToolScope.Board));
        _registry.RegisterTool(CreateTool("inbox.one", scope: ToolScope.Inbox));
        _registry.RegisterTool(CreateTool("board.two", scope: ToolScope.Board));
        _registry.RegisterTool(CreateTool("global.one", scope: ToolScope.Global));

        var boardTools = _registry.GetToolsByScope(ToolScope.Board);
        var inboxTools = _registry.GetToolsByScope(ToolScope.Inbox);
        var globalTools = _registry.GetToolsByScope(ToolScope.Global);

        boardTools.Should().HaveCount(2);
        inboxTools.Should().HaveCount(1);
        globalTools.Should().HaveCount(1);
    }

    [Fact]
    public void GetToolsByScope_ShouldReturnEmpty_WhenNoToolsMatchScope()
    {
        _registry.RegisterTool(CreateTool("board.one", scope: ToolScope.Board));

        _registry.GetToolsByScope(ToolScope.Global).Should().BeEmpty();
    }
}
