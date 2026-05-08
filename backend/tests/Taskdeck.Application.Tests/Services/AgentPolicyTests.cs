using FluentAssertions;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class AgentPolicyTests
{
    private readonly TaskdeckToolRegistry _toolRegistry;
    private readonly AgentPolicy _policy;

    public AgentPolicyTests()
    {
        _toolRegistry = new TaskdeckToolRegistry();
        _toolRegistry.RegisterTool(InboxTriageAssistant.GetToolDefinition());
        _policy = new AgentPolicy(_toolRegistry);
    }

    [Fact]
    public void ValidateToolBundle_AllowsRegisteredTool()
    {
        var decisions = _policy.ValidateToolBundle(new[] { "inbox.triage" });

        decisions.Should().HaveCount(1);
        decisions[0].Allowed.Should().BeTrue();
        decisions[0].ToolKey.Should().Be("inbox.triage");
    }

    [Fact]
    public void ValidateToolBundle_DeniesApproveProposal()
    {
        var decisions = _policy.ValidateToolBundle(new[] { "approve_proposal" });

        decisions.Should().HaveCount(1);
        decisions[0].Allowed.Should().BeFalse();
        decisions[0].Reason.Should().Contain("permanently excluded");
    }

    [Fact]
    public void ValidateToolBundle_DeniesApplyProposal()
    {
        var decisions = _policy.ValidateToolBundle(new[] { "apply_proposal" });

        decisions.Should().HaveCount(1);
        decisions[0].Allowed.Should().BeFalse();
        decisions[0].Reason.Should().Contain("permanently excluded");
    }

    [Fact]
    public void ValidateToolBundle_DeniesDirectBoardMutation()
    {
        var decisions = _policy.ValidateToolBundle(new[]
        {
            "board.direct_update",
            "board.direct_delete",
            "card.direct_move",
            "card.direct_delete",
            "column.direct_delete"
        });

        decisions.Should().HaveCount(5);
        decisions.Should().AllSatisfy(d => d.Allowed.Should().BeFalse());
    }

    [Fact]
    public void ValidateToolBundle_DeniesUnknownTool()
    {
        var decisions = _policy.ValidateToolBundle(new[] { "nonexistent_tool" });

        decisions.Should().HaveCount(1);
        decisions[0].Allowed.Should().BeFalse();
        decisions[0].Reason.Should().Contain("not registered");
    }

    [Fact]
    public void ValidateToolBundle_DeniesEmptyToolKey()
    {
        var decisions = _policy.ValidateToolBundle(new[] { "" });

        decisions.Should().HaveCount(1);
        decisions[0].Allowed.Should().BeFalse();
        decisions[0].Reason.Should().Contain("empty");
    }

    [Fact]
    public void ValidateToolBundle_HandlesMixedBundle()
    {
        var decisions = _policy.ValidateToolBundle(new[] { "inbox.triage", "approve_proposal", "unknown_tool" });

        decisions.Should().HaveCount(3);
        decisions[0].Allowed.Should().BeTrue();
        decisions[1].Allowed.Should().BeFalse();
        decisions[2].Allowed.Should().BeFalse();
    }

    [Fact]
    public void ValidateToolBundle_EmptyBundle_ReturnsEmpty()
    {
        var decisions = _policy.ValidateToolBundle(Array.Empty<string>());
        decisions.Should().BeEmpty();
    }

    [Fact]
    public void ValidateToolBundle_NullBundle_Throws()
    {
        var act = () => _policy.ValidateToolBundle(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PermanentlyExcludedTools_ContainsApproveProposal()
    {
        AgentPolicy.PermanentlyExcludedTools.Should().Contain("approve_proposal");
    }

    [Fact]
    public void PermanentlyExcludedTools_ContainsApplyProposal()
    {
        AgentPolicy.PermanentlyExcludedTools.Should().Contain("apply_proposal");
    }

    [Fact]
    public void PermanentlyExcludedTools_ContainsAllDirectMutations()
    {
        AgentPolicy.PermanentlyExcludedTools.Should().Contain("board.direct_update");
        AgentPolicy.PermanentlyExcludedTools.Should().Contain("board.direct_delete");
        AgentPolicy.PermanentlyExcludedTools.Should().Contain("card.direct_move");
        AgentPolicy.PermanentlyExcludedTools.Should().Contain("card.direct_delete");
        AgentPolicy.PermanentlyExcludedTools.Should().Contain("column.direct_delete");
    }

    [Fact]
    public void PermanentlyExcludedTools_IsCaseInsensitive()
    {
        var decisions = _policy.ValidateToolBundle(new[] { "APPROVE_PROPOSAL" });

        decisions[0].Allowed.Should().BeFalse();
        decisions[0].Reason.Should().Contain("permanently excluded");
    }

    [Fact]
    public void Constructor_NullRegistry_Throws()
    {
        var act = () => new AgentPolicy(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ValidateToolBundle_AllowedToolIncludesRiskLevel()
    {
        var decisions = _policy.ValidateToolBundle(new[] { "inbox.triage" });

        decisions[0].Reason.Should().Contain("Medium");
    }

    [Fact]
    public void ValidateToolBundle_MultipleRegisteredTools_AllAllowed()
    {
        _toolRegistry.RegisterTool(new TaskdeckToolDefinition(
            "board.list_columns", "List Columns", "Lists columns", ToolScope.Board, ToolRiskLevel.Low));

        var decisions = _policy.ValidateToolBundle(new[] { "inbox.triage", "board.list_columns" });

        decisions.Should().HaveCount(2);
        decisions.Should().AllSatisfy(d => d.Allowed.Should().BeTrue());
    }
}
