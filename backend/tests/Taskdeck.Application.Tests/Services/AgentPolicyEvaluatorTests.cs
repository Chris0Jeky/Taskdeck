using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Agents;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class AgentPolicyEvaluatorTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IAgentProfileRepository> _profileRepoMock;
    private readonly TaskdeckToolRegistry _toolRegistry;
    private readonly InMemoryLogger<AgentPolicyEvaluator> _logger;
    private readonly AgentPolicyEvaluator _evaluator;

    public AgentPolicyEvaluatorTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _profileRepoMock = new Mock<IAgentProfileRepository>();
        _unitOfWorkMock.Setup(u => u.AgentProfiles).Returns(_profileRepoMock.Object);

        _toolRegistry = new TaskdeckToolRegistry();
        _logger = new InMemoryLogger<AgentPolicyEvaluator>();
        _evaluator = new AgentPolicyEvaluator(_unitOfWorkMock.Object, _toolRegistry, _logger);
    }

    private static ITaskdeckTool CreateTool(
        string key, ToolRiskLevel riskLevel, ToolScope scope = ToolScope.Inbox)
    {
        return new TaskdeckToolDefinition(key, $"Tool {key}", $"Desc for {key}", scope, riskLevel);
    }

    private AgentProfile CreateProfile(
        string? policyJson = null,
        bool isEnabled = true)
    {
        var profile = new AgentProfile(
            Guid.NewGuid(),
            "Test Agent",
            "triage-v1",
            AgentScopeType.Workspace,
            policyJson: policyJson);

        if (!isEnabled)
            profile.SetEnabled(false);

        return profile;
    }

    [Fact]
    public async Task EvaluateToolUse_ShouldDeny_WhenAgentProfileIdIsEmpty()
    {
        var decision = await _evaluator.EvaluateToolUseAsync(Guid.Empty, "inbox.triage");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Contain("required");
    }

    [Fact]
    public async Task EvaluateToolUse_ShouldDeny_WhenToolKeyIsEmpty()
    {
        var decision = await _evaluator.EvaluateToolUseAsync(Guid.NewGuid(), "");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Contain("required");
    }

    [Fact]
    public async Task EvaluateToolUse_ShouldDeny_WhenToolNotInRegistry()
    {
        var profileId = Guid.NewGuid();

        var decision = await _evaluator.EvaluateToolUseAsync(profileId, "nonexistent.tool");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Contain("not registered");
    }

    [Fact]
    public async Task EvaluateToolUse_ShouldDeny_WhenProfileNotFound()
    {
        _toolRegistry.RegisterTool(CreateTool("inbox.triage", ToolRiskLevel.Medium));
        _profileRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentProfile?)null);

        var decision = await _evaluator.EvaluateToolUseAsync(Guid.NewGuid(), "inbox.triage");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Contain("not found");
    }

    [Fact]
    public async Task EvaluateToolUse_ShouldDeny_WhenProfileIsDisabled()
    {
        _toolRegistry.RegisterTool(CreateTool("inbox.triage", ToolRiskLevel.Medium));
        var profile = CreateProfile(isEnabled: false);
        _profileRepoMock.Setup(r => r.GetByIdAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var decision = await _evaluator.EvaluateToolUseAsync(profile.Id, "inbox.triage");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Contain("disabled");
    }

    [Fact]
    public async Task EvaluateToolUse_ShouldDeny_WhenToolNotInAllowlist()
    {
        _toolRegistry.RegisterTool(CreateTool("inbox.triage", ToolRiskLevel.Medium));
        _toolRegistry.RegisterTool(CreateTool("board.read", ToolRiskLevel.Low));

        var profile = CreateProfile(policyJson: "{\"allowedTools\":[\"board.read\"]}");
        _profileRepoMock.Setup(r => r.GetByIdAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var decision = await _evaluator.EvaluateToolUseAsync(profile.Id, "inbox.triage");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Contain("not in this agent's allowed tool list");
    }

    [Fact]
    public async Task EvaluateToolUse_ShouldAllow_WhenToolInAllowlist()
    {
        _toolRegistry.RegisterTool(CreateTool("inbox.triage", ToolRiskLevel.Medium));
        var profile = CreateProfile(policyJson: "{\"allowedTools\":[\"inbox.triage\"]}");
        _profileRepoMock.Setup(r => r.GetByIdAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var decision = await _evaluator.EvaluateToolUseAsync(profile.Id, "inbox.triage");

        decision.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateToolUse_ShouldAllow_WhenAllowlistIsEmpty()
    {
        _toolRegistry.RegisterTool(CreateTool("inbox.triage", ToolRiskLevel.Medium));
        var profile = CreateProfile(); // default "{}" policy - empty allowlist means all allowed
        _profileRepoMock.Setup(r => r.GetByIdAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var decision = await _evaluator.EvaluateToolUseAsync(profile.Id, "inbox.triage");

        decision.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateToolUse_ShouldRequireReview_ForHighRiskTool()
    {
        _toolRegistry.RegisterTool(CreateTool("board.delete", ToolRiskLevel.High));
        var profile = CreateProfile();
        _profileRepoMock.Setup(r => r.GetByIdAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var decision = await _evaluator.EvaluateToolUseAsync(profile.Id, "board.delete");

        decision.Allowed.Should().BeTrue();
        decision.RequiresReview.Should().BeTrue();
        decision.Reason.Should().Contain("High-risk");
    }

    [Fact]
    public async Task EvaluateToolUse_ShouldRequireReview_ForMediumRiskTool()
    {
        _toolRegistry.RegisterTool(CreateTool("inbox.triage", ToolRiskLevel.Medium));
        var profile = CreateProfile();
        _profileRepoMock.Setup(r => r.GetByIdAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var decision = await _evaluator.EvaluateToolUseAsync(profile.Id, "inbox.triage");

        decision.Allowed.Should().BeTrue();
        decision.RequiresReview.Should().BeTrue();
        decision.Reason.Should().Contain("Medium-risk");
    }

    [Fact]
    public async Task EvaluateToolUse_ShouldRequireReview_ForLowRiskTool_ByDefault()
    {
        _toolRegistry.RegisterTool(CreateTool("board.read-cards", ToolRiskLevel.Low));
        var profile = CreateProfile(); // no autoApplyLowRisk
        _profileRepoMock.Setup(r => r.GetByIdAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var decision = await _evaluator.EvaluateToolUseAsync(profile.Id, "board.read-cards");

        decision.Allowed.Should().BeTrue();
        decision.RequiresReview.Should().BeTrue();
        decision.Reason.Should().Contain("auto-apply is off");
    }

    [Fact]
    public async Task EvaluateToolUse_ShouldAllowDirect_ForLowRiskTool_WhenAutoApplyEnabled()
    {
        _toolRegistry.RegisterTool(CreateTool("board.read-cards", ToolRiskLevel.Low));
        var profile = CreateProfile(policyJson: "{\"autoApplyLowRisk\":true}");
        _profileRepoMock.Setup(r => r.GetByIdAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var decision = await _evaluator.EvaluateToolUseAsync(profile.Id, "board.read-cards");

        decision.Allowed.Should().BeTrue();
        decision.RequiresReview.Should().BeFalse();
        decision.Reason.Should().Contain("auto-applied");
    }

    [Fact]
    public async Task EvaluateToolUse_ShouldStillRequireReview_ForHighRisk_EvenWithAutoApply()
    {
        _toolRegistry.RegisterTool(CreateTool("board.delete", ToolRiskLevel.High));
        var profile = CreateProfile(policyJson: "{\"autoApplyLowRisk\":true}");
        _profileRepoMock.Setup(r => r.GetByIdAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var decision = await _evaluator.EvaluateToolUseAsync(profile.Id, "board.delete");

        decision.Allowed.Should().BeTrue();
        decision.RequiresReview.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateToolUse_ShouldLogDecisions()
    {
        _toolRegistry.RegisterTool(CreateTool("inbox.triage", ToolRiskLevel.Medium));
        var profile = CreateProfile();
        _profileRepoMock.Setup(r => r.GetByIdAsync(profile.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        await _evaluator.EvaluateToolUseAsync(profile.Id, "inbox.triage");

        _logger.Entries.Should().NotBeEmpty();
        _logger.Entries.Should().Contain(e => e.Message.Contains("inbox.triage"));
    }

    #region ParsePolicy edge cases

    [Fact]
    public void ParsePolicy_ShouldReturnDefaults_ForEmptyJson()
    {
        var config = AgentPolicyEvaluator.ParsePolicy("{}");

        config.AllowedTools.Should().BeEmpty();
        config.AutoApplyLowRisk.Should().BeFalse();
    }

    [Fact]
    public void ParsePolicy_ShouldReturnDefaults_ForMalformedJson()
    {
        var config = AgentPolicyEvaluator.ParsePolicy("not json");

        config.AllowedTools.Should().BeEmpty();
        config.AutoApplyLowRisk.Should().BeFalse();
    }

    [Fact]
    public void ParsePolicy_ShouldReturnDefaults_ForNull()
    {
        var config = AgentPolicyEvaluator.ParsePolicy(null);

        config.AllowedTools.Should().BeEmpty();
        config.AutoApplyLowRisk.Should().BeFalse();
    }

    [Fact]
    public void ParsePolicy_ShouldParseAllowedTools()
    {
        var config = AgentPolicyEvaluator.ParsePolicy(
            "{\"allowedTools\":[\"inbox.triage\",\"board.read\"]}");

        config.AllowedTools.Should().HaveCount(2);
        config.AllowedTools.Should().Contain("inbox.triage");
        config.AllowedTools.Should().Contain("board.read");
    }

    #endregion
}
