using FluentAssertions;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Agents;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class AgentPolicyTests
{
    private AgentPolicy CreatePolicy(params (string Key, ToolRiskLevel Risk)[] tools)
    {
        var registry = new TaskdeckToolRegistry();
        foreach (var (key, risk) in tools)
        {
            registry.RegisterTool(new TaskdeckToolDefinition(key, key, $"Test {key}", ToolScope.Board, risk));
        }
        return new AgentPolicy(registry);
    }

    [Fact]
    public void ValidateToolBundle_EmptyBundle_ReturnsEmpty()
    {
        var policy = CreatePolicy(("inbox.triage", ToolRiskLevel.Medium));
        var decisions = policy.ValidateToolBundle(Array.Empty<string>());
        decisions.Should().BeEmpty();
    }

    [Fact]
    public void ValidateToolBundle_RegisteredTool_IsAllowed()
    {
        var policy = CreatePolicy(("inbox.triage", ToolRiskLevel.Medium));
        var decisions = policy.ValidateToolBundle(new[] { "inbox.triage" });
        decisions.Should().HaveCount(1);
        decisions[0].Allowed.Should().BeTrue();
        decisions[0].ToolKey.Should().Be("inbox.triage");
    }

    [Fact]
    public void ValidateToolBundle_UnregisteredTool_IsDenied()
    {
        var policy = CreatePolicy(("inbox.triage", ToolRiskLevel.Medium));
        var decisions = policy.ValidateToolBundle(new[] { "unknown.tool" });
        decisions.Should().HaveCount(1);
        decisions[0].Allowed.Should().BeFalse();
        decisions[0].Reason.Should().Contain("not registered");
    }

    [Fact]
    public void ValidateToolBundle_ApproveProposal_IsPermanentlyExcluded()
    {
        var policy = CreatePolicy();
        var decisions = policy.ValidateToolBundle(new[] { "approve_proposal" });
        decisions.Should().HaveCount(1);
        decisions[0].Allowed.Should().BeFalse();
        decisions[0].Reason.Should().Contain("permanently excluded");
    }

    [Fact]
    public void ValidateToolBundle_ApplyProposal_IsPermanentlyExcluded()
    {
        var policy = CreatePolicy();
        var decisions = policy.ValidateToolBundle(new[] { "apply_proposal" });
        decisions.Should().HaveCount(1);
        decisions[0].Allowed.Should().BeFalse();
        decisions[0].Reason.Should().Contain("permanently excluded");
    }

    [Fact]
    public void ValidateToolBundle_DirectBoardMutation_IsPermanentlyExcluded()
    {
        var policy = CreatePolicy();
        var mutations = new[]
        {
            "board.direct_update", "board.direct_delete",
            "card.direct_move", "card.direct_delete",
            "column.direct_delete"
        };
        var decisions = policy.ValidateToolBundle(mutations);
        decisions.Should().HaveCount(5);
        decisions.Should().AllSatisfy(d => d.Allowed.Should().BeFalse());
    }

    [Fact]
    public void ValidateToolBundle_MixedBundle_ReturnsPerToolDecisions()
    {
        var policy = CreatePolicy(
            ("inbox.triage", ToolRiskLevel.Medium),
            ("board.read", ToolRiskLevel.Low));

        var decisions = policy.ValidateToolBundle(new[]
        {
            "inbox.triage", "approve_proposal", "board.read", "unknown"
        });

        decisions.Should().HaveCount(4);
        decisions[0].Allowed.Should().BeTrue();
        decisions[1].Allowed.Should().BeFalse(); // approve_proposal
        decisions[2].Allowed.Should().BeTrue();
        decisions[3].Allowed.Should().BeFalse(); // unknown
    }

    [Fact]
    public void ValidateToolBundle_NullToolKey_IsDenied()
    {
        var policy = CreatePolicy(("inbox.triage", ToolRiskLevel.Medium));
        var decisions = policy.ValidateToolBundle(new string[] { null! });
        decisions.Should().HaveCount(1);
        decisions[0].Allowed.Should().BeFalse();
    }

    [Fact]
    public void ValidateToolBundle_EmptyToolKey_IsDenied()
    {
        var policy = CreatePolicy(("inbox.triage", ToolRiskLevel.Medium));
        var decisions = policy.ValidateToolBundle(new[] { "" });
        decisions.Should().HaveCount(1);
        decisions[0].Allowed.Should().BeFalse();
    }

    [Fact]
    public void ValidateToolBundle_WithProfileAllowlist_RestrictsToAllowedTools()
    {
        var policy = CreatePolicy(
            ("inbox.triage", ToolRiskLevel.Medium),
            ("board.read", ToolRiskLevel.Low));

        var profileAllowlist = new List<string> { "inbox.triage" };
        var decisions = policy.ValidateToolBundle(
            new[] { "inbox.triage", "board.read" }, profileAllowlist);

        decisions.Should().HaveCount(2);
        decisions[0].Allowed.Should().BeTrue(); // inbox.triage in allowlist
        decisions[1].Allowed.Should().BeFalse(); // board.read not in allowlist
    }

    [Fact]
    public void ValidateToolBundle_EmptyProfileAllowlist_AllowsAll()
    {
        var policy = CreatePolicy(
            ("inbox.triage", ToolRiskLevel.Medium),
            ("board.read", ToolRiskLevel.Low));

        var decisions = policy.ValidateToolBundle(
            new[] { "inbox.triage", "board.read" }, new List<string>());

        decisions.Should().HaveCount(2);
        decisions.Should().AllSatisfy(d => d.Allowed.Should().BeTrue());
    }

    [Fact]
    public void IsPermanentlyExcluded_ApproveProposal_ReturnsTrue()
    {
        AgentPolicy.IsPermanentlyExcluded("approve_proposal").Should().BeTrue();
    }

    [Fact]
    public void IsPermanentlyExcluded_RegisteredTool_ReturnsFalse()
    {
        AgentPolicy.IsPermanentlyExcluded("inbox.triage").Should().BeFalse();
    }

    [Fact]
    public void IsPermanentlyExcluded_IsCaseInsensitive()
    {
        AgentPolicy.IsPermanentlyExcluded("APPROVE_PROPOSAL").Should().BeTrue();
        AgentPolicy.IsPermanentlyExcluded("Approve_Proposal").Should().BeTrue();
    }

    [Fact]
    public void GetPermanentlyExcludedTools_ContainsAllExpected()
    {
        var excluded = AgentPolicy.GetPermanentlyExcludedTools();
        excluded.Should().Contain("approve_proposal");
        excluded.Should().Contain("apply_proposal");
        excluded.Should().Contain("board.direct_update");
        excluded.Should().Contain("board.direct_delete");
        excluded.Should().Contain("card.direct_move");
        excluded.Should().Contain("card.direct_delete");
        excluded.Should().Contain("column.direct_delete");
    }

    [Fact]
    public void ValidateToolBundle_NullRequestedTools_Throws()
    {
        var policy = CreatePolicy();
        var act = () => policy.ValidateToolBundle(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ValidateToolBundle_ProfileAllowlistCaseInsensitive()
    {
        var policy = CreatePolicy(("inbox.triage", ToolRiskLevel.Medium));
        var profileAllowlist = new List<string> { "INBOX.TRIAGE" };
        var decisions = policy.ValidateToolBundle(new[] { "inbox.triage" }, profileAllowlist);
        decisions.Should().HaveCount(1);
        decisions[0].Allowed.Should().BeTrue();
    }
}
