using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class OpsCliService : IOpsCliService
{
    private readonly IUnitOfWork _unitOfWork;

    private static readonly Dictionary<string, CommandTemplateDto> _templates = new()
    {
        ["boards.list"] = new CommandTemplateDto("boards.list", "List all boards", "ReadOnly", 30, "admin", new List<string>()),
        ["boards.search"] = new CommandTemplateDto("boards.search", "Search boards by name", "ReadOnly", 30, "admin", new List<string> { "query" }),
        ["queue.stats"] = new CommandTemplateDto("queue.stats", "Get LLM queue statistics", "ReadOnly", 30, "admin", new List<string>()),
        ["queue.pending"] = new CommandTemplateDto("queue.pending", "List pending queue items", "ReadOnly", 30, "admin", new List<string>()),
        ["health.check"] = new CommandTemplateDto("health.check", "Run health check", "ReadOnly", 30, "editor", new List<string>())
    };

    public OpsCliService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CommandRunDto>> RunCommandAsync(Guid userId, RunCommandDto dto, CancellationToken ct = default)
    {
        try
        {
            if (!_templates.ContainsKey(dto.TemplateName))
                return Result.Failure<CommandRunDto>(ErrorCodes.ValidationError, $"Unknown command template: {dto.TemplateName}");

            var correlationId = Guid.NewGuid().ToString("N");

            var commandRun = new CommandRun(dto.TemplateName, userId, correlationId);
            await _unitOfWork.CommandRuns.AddAsync(commandRun, ct);

            commandRun.Start();

            try
            {
                var output = await ExecuteTemplateAsync(dto.TemplateName, dto.Parameters, ct);

                if (output.Length > 1000)
                {
                    commandRun.SetOutputPreview(output[..1000]);
                    commandRun.SetTruncated();
                }
                else
                {
                    commandRun.SetOutputPreview(output);
                }

                commandRun.AddLog(new CommandRunLog(commandRun.Id, "Info", "OpsCliService", $"Command '{dto.TemplateName}' completed successfully"));
                commandRun.Complete(0);
            }
            catch (Exception ex)
            {
                commandRun.AddLog(new CommandRunLog(commandRun.Id, "Error", "OpsCliService", $"Command '{dto.TemplateName}' failed: {ex.Message}"));
                commandRun.Fail(ex.Message);
            }

            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Success(MapToDto(commandRun));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CommandRunDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<CommandRunDetailDto>> GetCommandRunAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _unitOfWork.CommandRuns.GetByIdWithLogsAsync(runId, ct);
        if (run == null)
            return Result.Failure<CommandRunDetailDto>(ErrorCodes.NotFound, $"Command run with ID {runId} not found");

        return Result.Success(MapToDetailDto(run));
    }

    public async Task<Result<IEnumerable<CommandRunLogDto>>> GetCommandRunLogsAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _unitOfWork.CommandRuns.GetByIdWithLogsAsync(runId, ct);
        if (run == null)
            return Result.Failure<IEnumerable<CommandRunLogDto>>(ErrorCodes.NotFound, $"Command run with ID {runId} not found");

        return Result.Success(run.Logs.Select(MapLogToDto));
    }

    public Result<IEnumerable<CommandTemplateDto>> GetAvailableTemplates()
    {
        return Result.Success<IEnumerable<CommandTemplateDto>>(_templates.Values.ToList());
    }

    private async Task<string> ExecuteTemplateAsync(string templateName, Dictionary<string, string>? parameters, CancellationToken ct)
    {
        return templateName switch
        {
            "boards.list" => await ExecuteBoardsListAsync(ct),
            "boards.search" => await ExecuteBoardsSearchAsync(parameters, ct),
            "queue.stats" => await ExecuteQueueStatsAsync(),
            "queue.pending" => await ExecuteQueuePendingAsync(),
            "health.check" => ExecuteHealthCheck(),
            _ => throw new InvalidOperationException($"No handler for template: {templateName}")
        };
    }

    private async Task<string> ExecuteBoardsListAsync(CancellationToken ct)
    {
        var boards = await _unitOfWork.Boards.GetAllAsync(ct);
        var boardList = boards.ToList();
        return $"Found {boardList.Count} board(s).\n" + string.Join("\n", boardList.Select(b => $"- {b.Name} ({b.Id})"));
    }

    private async Task<string> ExecuteBoardsSearchAsync(Dictionary<string, string>? parameters, CancellationToken ct)
    {
        var query = parameters?.GetValueOrDefault("query") ?? "";
        var boards = await _unitOfWork.Boards.GetAllAsync(ct);
        var matches = boards.Where(b => b.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        return $"Search for '{query}': {matches.Count} result(s).\n" + string.Join("\n", matches.Select(b => $"- {b.Name} ({b.Id})"));
    }

    private async Task<string> ExecuteQueueStatsAsync()
    {
        var pending = (await _unitOfWork.LlmQueue.GetByStatusAsync(Domain.Enums.RequestStatus.Pending)).Count();
        var processing = (await _unitOfWork.LlmQueue.GetByStatusAsync(Domain.Enums.RequestStatus.Processing)).Count();
        var completed = (await _unitOfWork.LlmQueue.GetByStatusAsync(Domain.Enums.RequestStatus.Completed)).Count();
        var failed = (await _unitOfWork.LlmQueue.GetByStatusAsync(Domain.Enums.RequestStatus.Failed)).Count();
        return $"Queue stats: Pending={pending}, Processing={processing}, Completed={completed}, Failed={failed}";
    }

    private async Task<string> ExecuteQueuePendingAsync()
    {
        var pending = await _unitOfWork.LlmQueue.GetByStatusAsync(Domain.Enums.RequestStatus.Pending);
        var pendingList = pending.ToList();
        return $"Pending queue items: {pendingList.Count}\n" + string.Join("\n", pendingList.Select(r => $"- {r.Id}: {r.RequestType} (created: {r.CreatedAt})"));
    }

    private static string ExecuteHealthCheck()
    {
        return $"Health check: OK\nTimestamp: {DateTime.UtcNow:O}\nStatus: Healthy";
    }

    private static CommandRunDto MapToDto(CommandRun run)
    {
        return new CommandRunDto(
            run.Id,
            run.TemplateName,
            run.RequestedByUserId,
            run.Status,
            run.StartedAt,
            run.CompletedAt,
            run.ExitCode,
            run.Truncated,
            run.CorrelationId,
            run.ErrorMessage,
            run.OutputPreview,
            run.CreatedAt
        );
    }

    private static CommandRunDetailDto MapToDetailDto(CommandRun run)
    {
        return new CommandRunDetailDto(
            run.Id,
            run.TemplateName,
            run.RequestedByUserId,
            run.Status,
            run.StartedAt,
            run.CompletedAt,
            run.ExitCode,
            run.Truncated,
            run.CorrelationId,
            run.ErrorMessage,
            run.OutputPreview,
            run.CreatedAt,
            run.Logs.Select(MapLogToDto).ToList()
        );
    }

    private static CommandRunLogDto MapLogToDto(CommandRunLog log)
    {
        return new CommandRunLogDto(
            log.Id,
            log.CommandRunId,
            log.Timestamp,
            log.Level,
            log.Source,
            log.Message,
            log.Metadata
        );
    }
}
