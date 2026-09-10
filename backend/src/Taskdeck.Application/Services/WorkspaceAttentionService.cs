using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class WorkspaceAttentionService(IWorkspaceAttentionRepository repository, WorkspaceInsightService insights, TimeProvider clock)
{
    public async Task<WorkspaceAttentionDto> GetAsync(Guid user, CancellationToken ct)
    {
        var row = await repository.GetAsync(user, ct);
        return Map(row.ReadAttention(), row.AttentionRevision);
    }

    public async Task<Result<WorkspaceAttentionDto>> SaveAsync(Guid user, SaveWorkspaceAttentionDto dto, CancellationToken ct)
    {
        var row = await repository.GetAsync(user, ct);
        if (row.AttentionRevision != dto.ExpectedRevision) return Conflict<WorkspaceAttentionDto>();
        // Toggling the preference never resets a consumed budget.
        var next = row.ReadAttention() with { Enabled = dto.Enabled };
        if (!await repository.SaveAsync(user, dto.ExpectedRevision, next, ct)) return Conflict<WorkspaceAttentionDto>();
        return Result.Success(Map(next, dto.ExpectedRevision + 1));
    }

    public async Task<Result<WorkspaceAttentionReminderDto?>> ClaimAsync(Guid user, Guid boardId, CancellationToken ct)
    {
        var row = await repository.GetAsync(user, ct);
        var state = row.ReadAttention(); var now = clock.GetUtcNow();
        if (!state.CanClaim(now)) return Result.Success<WorkspaceAttentionReminderDto?>(null);
        // Revalidate previously saved questions; this never analyzes or calls a model.
        var existing = await insights.ListAsync(user, boardId, false, ct);
        if (!existing.IsSuccess) return Result.Failure<WorkspaceAttentionReminderDto?>(existing.ErrorCode, existing.ErrorMessage);
        var candidate = existing.Value.FirstOrDefault(x => x.State == "available" && x.Id != state.LastInsightId);
        var next = candidate is null ? null : state.Claim(candidate.Id, now);
        if (next is null) return Result.Success<WorkspaceAttentionReminderDto?>(null);
        if (!await repository.SaveAsync(user, row.AttentionRevision, next, ct)) return Result.Success<WorkspaceAttentionReminderDto?>(null);
        return Result.Success<WorkspaceAttentionReminderDto?>(new(boardId, candidate!.Id));
    }

    private static WorkspaceAttentionDto Map(WorkspaceAttention state, long revision) =>
        new(state.Enabled, revision, WorkspaceAttention.DailyLimit, (int)WorkspaceAttention.MinimumSpacing.TotalMinutes);
    private static Result<T> Conflict<T>() => Result.Failure<T>(ErrorCodes.Conflict, "Your reminder preference changed. Reload before trying again.");
}
