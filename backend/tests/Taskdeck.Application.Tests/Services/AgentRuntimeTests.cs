using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Agents;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class AgentRuntimeTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAgentProfileRepository> _profileRepo = new();
    private readonly Mock<IAgentRunRepository> _runRepo = new();
    private readonly Mock<IAgentPolicyEvaluator> _policyEvaluator = new();
    private readonly TaskdeckToolRegistry _toolRegistry = new();
    private readonly AgentPolicy _agentPolicy;
    private readonly AgentRuntime _runtime;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _profileId = Guid.NewGuid();

    public AgentRuntimeTests()
    {
        _toolRegistry.RegisterTool(InboxTriageAssistant.GetToolDefinition());

        _agentPolicy = new AgentPolicy(_toolRegistry);

        _unitOfWork.Setup(u => u.AgentProfiles).Returns(_profileRepo.Object);
        _unitOfWork.Setup(u => u.AgentRuns).Returns(_runRepo.Object);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _runtime = new AgentRuntime(
            _unitOfWork.Object,
            _agentPolicy,
            _policyEvaluator.Object);
    }

    private AgentProfile CreateProfile(bool enabled = true, string? policyJson = null)
    {
        return new AgentProfile(
            _userId, "Test Agent", "inbox-triage-digest",
            AgentScopeType.Workspace, description: "Test",
            policyJson: policyJson);
    }

    [Fact]
    public async Task RunAsync_ProfileNotFound_ReturnsFailure()
    {
        _profileRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentProfile?)null);

        var result = await _runtime.RunAsync(
            _profileId, _userId, "test objective",
            new[] { "inbox.triage" },
            (run, step, ct) => Task.FromResult(new AgentStepResult("test", IsTerminal: true)));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NotFound");
    }

    [Fact]
    public async Task RunAsync_WrongUser_ReturnsFailure()
    {
        var profile = CreateProfile();
        _profileRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var result = await _runtime.RunAsync(
            _profileId, Guid.NewGuid(), "test objective",
            new[] { "inbox.triage" },
            (run, step, ct) => Task.FromResult(new AgentStepResult("test", IsTerminal: true)));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("Forbidden");
    }

    [Fact]
    public async Task RunAsync_DisabledProfile_ReturnsFailure()
    {
        var profile = CreateProfile(enabled: false);
        profile.SetEnabled(false);
        _profileRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        var result = await _runtime.RunAsync(
            profile.Id, _userId, "test objective",
            new[] { "inbox.triage" },
            (run, step, ct) => Task.FromResult(new AgentStepResult("test", IsTerminal: true)));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("InvalidOperation");
    }

    [Fact]
    public async Task RunAsync_ExcludedTool_ReturnsFailure()
    {
        var profile = CreateProfile();
        _profileRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _runRepo.Setup(r => r.GetActiveByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<AgentRun>());

        var result = await _runtime.RunAsync(
            profile.Id, _userId, "test objective",
            new[] { "approve_proposal" },
            (run, step, ct) => Task.FromResult(new AgentStepResult("test", IsTerminal: true)));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("Forbidden");
        result.ErrorMessage.Should().Contain("permanently excluded");
    }

    [Fact]
    public async Task RunAsync_UnknownTool_ReturnsFailure()
    {
        var profile = CreateProfile();
        _profileRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _runRepo.Setup(r => r.GetActiveByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<AgentRun>());

        var result = await _runtime.RunAsync(
            profile.Id, _userId, "test objective",
            new[] { "nonexistent_tool" },
            (run, step, ct) => Task.FromResult(new AgentStepResult("test", IsTerminal: true)));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("Forbidden");
    }

    [Fact]
    public async Task RunAsync_ValidRun_CompletesSuccessfully()
    {
        var profile = CreateProfile();
        _profileRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _runRepo.Setup(r => r.GetActiveByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<AgentRun>());

        var proposalId = Guid.NewGuid();
        var result = await _runtime.RunAsync(
            profile.Id, _userId, "triage inbox",
            new[] { "inbox.triage" },
            (run, step, ct) => Task.FromResult(new AgentStepResult(
                EventType: "triage.done",
                Payload: "{}",
                IsTerminal: true,
                ProposalId: proposalId,
                Summary: "Triaged 5 items")));

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(AgentRunStatus.ProposalCreated);
        result.Value.ProposalId.Should().Be(proposalId);
    }

    [Fact]
    public async Task RunAsync_MaxStepsExhausted_CompletesWithQuotaMessage()
    {
        var profile = CreateProfile();
        _profileRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _runRepo.Setup(r => r.GetActiveByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<AgentRun>());

        var stepCount = 0;
        var result = await _runtime.RunAsync(
            profile.Id, _userId, "run many steps",
            new[] { "inbox.triage" },
            (run, step, ct) =>
            {
                stepCount++;
                return Task.FromResult(new AgentStepResult("step.working"));
            },
            maxSteps: 3);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(AgentRunStatus.Completed);
        result.Value.Summary.Should().Contain("Step quota exhausted");
        stepCount.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_ConcurrentRunQuota_ReturnsFailure()
    {
        var profile = CreateProfile();
        _profileRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        // Simulate max concurrent runs already active
        var activeRuns = Enumerable.Range(0, AgentRuntime.DefaultMaxConcurrentRunsPerUser)
            .Select(_ => new AgentRun(profile.Id, _userId, "active run"));
        _runRepo.Setup(r => r.GetActiveByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeRuns);

        var result = await _runtime.RunAsync(
            profile.Id, _userId, "new run",
            new[] { "inbox.triage" },
            (run, step, ct) => Task.FromResult(new AgentStepResult("test", IsTerminal: true)));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("TooManyRequests");
    }

    [Fact]
    public async Task RunAsync_Cancellation_MarksCancelled()
    {
        var profile = CreateProfile();
        _profileRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _runRepo.Setup(r => r.GetActiveByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<AgentRun>());

        using var cts = new CancellationTokenSource();

        var result = await _runtime.RunAsync(
            profile.Id, _userId, "will be cancelled",
            new[] { "inbox.triage" },
            (run, step, ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new AgentStepResult("test", IsTerminal: true));
            },
            cancellationToken: cts.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(AgentRunStatus.Cancelled);
    }

    [Fact]
    public async Task RunAsync_EgressViolation_MarksFailedWithViolation()
    {
        var profile = CreateProfile();
        _profileRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _runRepo.Setup(r => r.GetActiveByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<AgentRun>());

        var result = await _runtime.RunAsync(
            profile.Id, _userId, "will violate egress",
            new[] { "inbox.triage" },
            (run, step, ct) =>
            {
                var violation = new EgressViolation(
                    "attacker.example", "https://attacker.example",
                    EgressViolationType.UnknownHost, "Not in envelope");
                throw new EgressViolationException(violation);
            });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("Forbidden");
        result.ErrorMessage.Should().Contain("Egress violation");
    }

    [Fact]
    public async Task RunAsync_RecordsEvents()
    {
        var profile = CreateProfile();
        _profileRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _runRepo.Setup(r => r.GetActiveByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<AgentRun>());

        var eventsRecorded = new List<AgentRunEvent>();
        _runRepo.Setup(r => r.AddEventAsync(It.IsAny<AgentRunEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRunEvent, CancellationToken>((e, _) => eventsRecorded.Add(e));

        var result = await _runtime.RunAsync(
            profile.Id, _userId, "test",
            new[] { "inbox.triage" },
            (run, step, ct) => Task.FromResult(new AgentStepResult(
                "test.step", Payload: "{}", IsTerminal: true,
                Summary: "Done")));

        result.IsSuccess.Should().BeTrue();
        eventsRecorded.Should().HaveCountGreaterOrEqualTo(2); // policy + step
        eventsRecorded[0].EventType.Should().Be("policy.validated");
    }

    [Fact]
    public void DefaultConstants_AreReasonable()
    {
        AgentRuntime.DefaultMaxStepsPerRun.Should().BeGreaterThan(0);
        AgentRuntime.DefaultMaxTokensPerRun.Should().BeGreaterThan(0);
        AgentRuntime.DefaultMaxConcurrentRunsPerUser.Should().BeGreaterThan(0);
    }
}
