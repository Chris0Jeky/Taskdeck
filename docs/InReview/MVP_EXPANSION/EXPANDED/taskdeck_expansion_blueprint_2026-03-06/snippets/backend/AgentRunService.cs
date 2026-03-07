using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

public interface IAgentRunService
{
    Task<Result<AgentRunDto>> StartManualRunAsync(Guid agentId, Guid actorUserId, StartAgentRunDto dto, CancellationToken ct = default);
    Task<Result<AgentRunDto>> GetRunAsync(Guid runId, Guid actorUserId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<AgentRunEventDto>>> GetRunEventsAsync(Guid runId, Guid actorUserId, CancellationToken ct = default);
}

public sealed class AgentRunService : IAgentRunService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAgentPolicyEvaluator _policyEvaluator;
    private readonly ITaskdeckToolRegistry _toolRegistry;
    private readonly IAutomationPlannerService _planner;

    public AgentRunService(
        IUnitOfWork unitOfWork,
        IAgentPolicyEvaluator policyEvaluator,
        ITaskdeckToolRegistry toolRegistry,
        IAutomationPlannerService planner)
    {
        _unitOfWork = unitOfWork;
        _policyEvaluator = policyEvaluator;
        _toolRegistry = toolRegistry;
        _planner = planner;
    }

    public async Task<Result<AgentRunDto>> StartManualRunAsync(Guid agentId, Guid actorUserId, StartAgentRunDto dto, CancellationToken ct = default)
    {
        // Sketch:
        // 1. load agent and authorize ownership/scope
        // 2. create run entity
        // 3. gather context summary (board, captures, due items, etc.)
        // 4. choose a narrow action plan based on template and policy
        // 5. either create a proposal or artifact
        // 6. persist run + events + linkage
        // 7. return mapped dto
        throw new NotImplementedException();
    }

    public Task<Result<AgentRunDto>> GetRunAsync(Guid runId, Guid actorUserId, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<Result<IReadOnlyList<AgentRunEventDto>>> GetRunEventsAsync(Guid runId, Guid actorUserId, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}
