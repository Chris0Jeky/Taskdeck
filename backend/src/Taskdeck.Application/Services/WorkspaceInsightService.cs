using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class WorkspaceInsightService(IWorkspaceInsightRepository repository, IUnitOfWork unitOfWork, IAuthorizationService authorization, ICaptureStore captureStore)
{
    private static Result<T> Missing<T>() => Result.Failure<T>(ErrorCodes.NotFound, "This workspace record is unavailable.");
    private static Result<T> Conflict<T>() => Result.Failure<T>(ErrorCodes.Conflict, "The workspace changed. Reload and try again.");
    private async Task<bool> CanRead(Guid userId, Guid boardId, CancellationToken ct)
    {
        var access = await authorization.CanReadBoardAsync(userId, boardId);
        if (!access.IsSuccess || !access.Value) return false;
        var board = await unitOfWork.Boards.GetByIdAsync(boardId, ct);
        return board is { IsArchived: false };
    }

    public async Task<Result<List<QuietInsightDto>>> ListAsync(Guid userId, Guid boardId, bool analyze, CancellationToken ct)
    {
        if (!await CanRead(userId, boardId, ct)) return Missing<List<QuietInsightDto>>();
        var items = await Revalidate(userId, boardId, analyze, ct);
        if (!await repository.SaveAsync(ct)) return Conflict<List<QuietInsightDto>>();
        return Result.Success(items.OrderByDescending(x => x.CreatedAt).Select(Map).ToList());
    }

    private async Task<List<QuietInsight>> Revalidate(Guid userId, Guid boardId, bool create, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var cards = (await unitOfWork.Cards.GetByBoardIdAsync(boardId, ct)).ToList();
        var memories = await repository.MemoriesAsync(userId, boardId, ct);
        var items = await repository.InsightsAsync(userId, boardId, ct);
        var seen = new HashSet<Guid>();
        void Keep(string rule, string target, Guid? cardId, Guid? memoryId, string title, string detail, string evidence)
        {
            var item = items.FirstOrDefault(x => x.Rule == rule && x.TargetKey == target);
            if (item == null)
            {
                if (!create) return;
                item = new QuietInsight(userId, boardId, rule, target, cardId, memoryId);
                repository.Add(item); items.Add(item);
            }
            item.Refresh(title, detail, evidence, now);
            if (items.Any(x => x.Rule == rule && x.State == "muted")) item.Act("mute", now);
            seen.Add(item.Id);
        }
        foreach (var card in cards.Where(x => x.IsBlocked))
        {
            // A retained answer closes this question without changing the actual blocked card.
            var prior = items.FirstOrDefault(x => x.Rule == "blocked-next-step" && x.TargetKey == card.Id.ToString());
            var evidence = $"Blocked: {card.BlockReason}. Card updated {card.UpdatedAt:O}.";
            if (prior != null && memories.Any(m => m.InsightId == prior.Id && m.OriginalEvidence == evidence && !m.Archived && m.Status == "statement")) continue;
            Keep("blocked-next-step", card.Id.ToString(), card.Id, null,
                $"What would unblock “{card.Title}”?", "This card is explicitly marked blocked. Keep a useful next step or record what is still unknown; its board status will stay unchanged.",
                evidence);
        }
        foreach (var memory in memories.Where(x => !x.Archived && x.Status is "needsReview" or "unknown"))
            Keep("memory-review", memory.Id.ToString(), null, memory.Id, $"Revisit “{memory.Title}”",
                memory.Status == "unknown" ? "This is an explicitly recorded unknown. Has anything become clearer?" : "You marked this memory for review. Confirm or correct it before relying on it.",
                $"Memory revision {memory.Revision}: {memory.Text}");
        foreach (var item in items.Where(x => !seen.Contains(x.Id))) item.Resolve(now);
        return items;
    }

    public async Task<Result<QuietInsightDto>> ActAsync(Guid userId, Guid id, string action, CancellationToken ct)
    {
        var item = await repository.InsightAsync(userId, id, ct);
        if (item == null || !await CanRead(userId, item.BoardId, ct)) return Missing<QuietInsightDto>();
        try
        {
            var items = await Revalidate(userId, item.BoardId, false, ct);
            if (action is "mute" || (action == "reopen" && item.State == "muted"))
                foreach (var related in items.Where(x => x.Rule == item.Rule)) related.Act(action, DateTimeOffset.UtcNow);
            else item.Act(action, DateTimeOffset.UtcNow);
            // Reopen and expired snoozes must still match a current cause.
            await Revalidate(userId, item.BoardId, false, ct);
            return await repository.SaveAsync(ct) ? Result.Success(Map(item)) : Conflict<QuietInsightDto>();
        }
        catch (DomainException ex) { return Result.Failure<QuietInsightDto>(ex.ErrorCode, ex.Message); }
    }

    public async Task<Result<List<WorkspaceMemoryDto>>> MemoriesAsync(Guid userId, Guid boardId, bool archived, CancellationToken ct)
    {
        if (!await CanRead(userId, boardId, ct)) return Missing<List<WorkspaceMemoryDto>>();
        var memories = await repository.MemoriesAsync(userId, boardId, ct);
        return Result.Success(memories.Where(x => x.Archived == archived).OrderByDescending(x => x.UpdatedAt).Select(MapMemory).ToList());
    }

    public async Task<Result<WorkspaceMemoryDto>> CreateAsync(Guid userId, CreateWorkspaceMemoryDto dto, CancellationToken ct)
    {
        if (!await CanRead(userId, dto.BoardId, ct)) return Missing<WorkspaceMemoryDto>();
        try
        {
            var memory = new WorkspaceMemory(userId, dto.BoardId, dto.Title, dto.Text, dto.Status);
            await new CaptureIntakeService(captureStore, null).StageMemorySourcesAsync(memory, ct);
            repository.Add(memory);
            return await repository.SaveAsync(ct) ? Result.Success(MapMemory(memory)) : Conflict<WorkspaceMemoryDto>();
        }
        catch (DomainException ex) { return Result.Failure<WorkspaceMemoryDto>(ex.ErrorCode, ex.Message); }
    }

    public async Task<Result<UserDataExportNativeCaptureDto>> SourcesAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var memory = await repository.MemoryAsync(userId, id, ct);
        if (memory == null || !await CanRead(userId, memory.BoardId, ct) || memory.SourceCaptureId is not { } captureId)
            return Missing<UserDataExportNativeCaptureDto>();
        var capture = await captureStore.GetByIdForUserAsync(captureId, userId, ct);
        return capture == null ? Missing<UserDataExportNativeCaptureDto>()
            : Result.Success(new UserDataExportNativeCaptureDto(capture.Id, capture.ContextBoardId, DataExportService.MapDurableCapture(capture)!));
    }

    public async Task<Result<WorkspaceMemoryDto>> AnswerAsync(Guid userId, Guid id, AnswerInsightDto dto, CancellationToken ct)
    {
        var item = await repository.InsightAsync(userId, id, ct);
        if (item == null || !await CanRead(userId, item.BoardId, ct)) return Missing<WorkspaceMemoryDto>();
        await Revalidate(userId, item.BoardId, false, ct);
        if (item.State != "available" || item.Evidence != dto.Evidence) return Conflict<WorkspaceMemoryDto>();
        try
        {
            WorkspaceMemory memory;
            if (item.MemoryId.HasValue)
            {
                memory = (await repository.MemoryAsync(userId, item.MemoryId.Value, ct))!;
                if (memory == null || memory.Archived) return Conflict<WorkspaceMemoryDto>();
                memory.Revise(memory.Title, dto.Text, dto.Status);
            }
            else
            {
                memory = new WorkspaceMemory(userId, item.BoardId, item.Title, dto.Text, dto.Status, item.Id, item.Evidence);
                repository.Add(memory);
            }
            await new CaptureIntakeService(captureStore, null).StageMemorySourcesAsync(memory, ct);
            if (dto.Status == "statement") item.Resolve(DateTimeOffset.UtcNow);
            else item.Act("snooze", DateTimeOffset.UtcNow);
            return await repository.SaveAsync(ct) ? Result.Success(MapMemory(memory)) : Conflict<WorkspaceMemoryDto>();
        }
        catch (DomainException ex) { return Result.Failure<WorkspaceMemoryDto>(ex.ErrorCode, ex.Message); }
    }

    public async Task<Result<WorkspaceMemoryDto>> UpdateAsync(Guid userId, Guid id, UpdateWorkspaceMemoryDto? dto, ArchiveWorkspaceMemoryDto? archive, CancellationToken ct)
    {
        var memory = await repository.MemoryAsync(userId, id, ct);
        if (memory == null || !await CanRead(userId, memory.BoardId, ct)) return Missing<WorkspaceMemoryDto>();
        if (memory.Revision != (dto?.Revision ?? archive?.Revision)) return Conflict<WorkspaceMemoryDto>();
        try
        {
            var priorRevision = memory.Revision;
            if (dto != null) memory.Revise(dto.Title, dto.Text, dto.Status);
            else memory.SetArchived(archive!.Archived);
            // A no-op archive does not advance CAS, so it must not attach competing native captures.
            if (memory.Revision != priorRevision)
                await new CaptureIntakeService(captureStore, null).StageMemorySourcesAsync(memory, ct);
            await Revalidate(userId, memory.BoardId, false, ct);
            return await repository.SaveAsync(ct) ? Result.Success(MapMemory(memory)) : Conflict<WorkspaceMemoryDto>();
        }
        catch (DomainException ex) { return Result.Failure<WorkspaceMemoryDto>(ex.ErrorCode, ex.Message); }
    }

    private static QuietInsightDto Map(QuietInsight x) => new(x.Id, x.BoardId, x.CardId, x.MemoryId, x.Rule, x.Title, x.Detail, x.State, x.Evidence, x.CheckedAt, x.SnoozeUntil);
    internal static WorkspaceMemoryDto MapMemory(WorkspaceMemory x) => new(x.Id, x.BoardId, x.Title, x.Text, x.OriginalText, x.Status, x.Archived, x.Revision, x.CreatedAt,
        x.History.OrderByDescending(h => h.Revision).Select(h => new WorkspaceMemoryRevisionDto(h.Title, h.Text, h.Status, h.Revision, h.Archived, h.CreatedAt, h.AnswerSourceAssetId)).ToList(), x.OriginalEvidence,
        x.SourceCardId.HasValue && x.SourceLayerId.HasValue && x.SourceDeckRevision.HasValue ? new ThinkingAnswerSourceDto(x.SourceCardId.Value, x.SourceLayerId.Value, x.SourceDeckRevision.Value) : null,
        x.SourceCaptureId.HasValue && x.AnswerSourceAssetId.HasValue ? new WorkspaceMemorySourcesDto(x.SourceCaptureId.Value, x.AnswerSourceAssetId.Value, x.EvidenceSourceAssetId) : null);
}
