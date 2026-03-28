using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class AgentRunService
{
    private readonly IUnitOfWork _unitOfWork;

    public AgentRunService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AgentRunDto>> CreateRunAsync(
        Guid agentProfileId,
        Guid userId,
        CreateAgentRunDto dto,
        CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.AgentProfiles.GetByIdAsync(agentProfileId, cancellationToken);
        if (profile is null)
            return Result.Failure<AgentRunDto>(ErrorCodes.NotFound, "Agent profile not found");

        if (profile.UserId != userId)
            return Result.Failure<AgentRunDto>(ErrorCodes.Forbidden, "Access denied to this agent profile");

        if (!profile.IsEnabled)
            return Result.Failure<AgentRunDto>(ErrorCodes.InvalidOperation, "Agent profile is disabled");

        try
        {
            var boardId = dto.BoardId ?? profile.ScopeBoardId;

            var run = new AgentRun(
                agentProfileId,
                userId,
                dto.Objective,
                triggerType: "manual",
                boardId: boardId);

            await _unitOfWork.AgentRuns.AddAsync(run, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(run));
        }
        catch (DomainException ex)
        {
            return Result.Failure<AgentRunDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<IEnumerable<AgentRunDto>>> GetRunsForProfileAsync(
        Guid agentProfileId,
        Guid userId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.AgentProfiles.GetByIdAsync(agentProfileId, cancellationToken);
        if (profile is null)
            return Result.Failure<IEnumerable<AgentRunDto>>(ErrorCodes.NotFound, "Agent profile not found");

        if (profile.UserId != userId)
            return Result.Failure<IEnumerable<AgentRunDto>>(ErrorCodes.Forbidden, "Access denied to this agent profile");

        var runs = await _unitOfWork.AgentRuns.GetByAgentProfileIdAsync(agentProfileId, limit, cancellationToken);
        return Result.Success(runs.Select(MapToDto));
    }

    public async Task<Result<AgentRunDetailDto>> GetRunWithEventsAsync(
        Guid agentProfileId,
        Guid runId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _unitOfWork.AgentProfiles.GetByIdAsync(agentProfileId, cancellationToken);
        if (profile is null)
            return Result.Failure<AgentRunDetailDto>(ErrorCodes.NotFound, "Agent profile not found");

        if (profile.UserId != userId)
            return Result.Failure<AgentRunDetailDto>(ErrorCodes.Forbidden, "Access denied to this agent profile");

        var run = await _unitOfWork.AgentRuns.GetByIdWithEventsAsync(runId, cancellationToken);
        if (run is null)
            return Result.Failure<AgentRunDetailDto>(ErrorCodes.NotFound, "Agent run not found");

        if (run.AgentProfileId != agentProfileId)
            return Result.Failure<AgentRunDetailDto>(ErrorCodes.NotFound, "Agent run not found for this profile");

        return Result.Success(MapToDetailDto(run));
    }

    private static AgentRunDto MapToDto(AgentRun run)
    {
        return new AgentRunDto(
            run.Id,
            run.AgentProfileId,
            run.UserId,
            run.BoardId,
            run.TriggerType,
            run.Objective,
            run.Status,
            run.Summary,
            run.FailureReason,
            run.ProposalId,
            run.StepsExecuted,
            run.TokensUsed,
            run.ApproxCostUsd,
            run.StartedAt,
            run.CompletedAt,
            run.CreatedAt,
            run.UpdatedAt);
    }

    private static AgentRunDetailDto MapToDetailDto(AgentRun run)
    {
        return new AgentRunDetailDto(
            run.Id,
            run.AgentProfileId,
            run.UserId,
            run.BoardId,
            run.TriggerType,
            run.Objective,
            run.Status,
            run.Summary,
            run.FailureReason,
            run.ProposalId,
            run.StepsExecuted,
            run.TokensUsed,
            run.ApproxCostUsd,
            run.StartedAt,
            run.CompletedAt,
            run.CreatedAt,
            run.UpdatedAt,
            run.Events.OrderBy(e => e.SequenceNumber).Select(e => new AgentRunEventDto(
                e.Id,
                e.RunId,
                e.SequenceNumber,
                e.EventType,
                e.Payload,
                e.Timestamp)).ToList());
    }
}
