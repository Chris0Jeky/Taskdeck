using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class OpsCliService : IOpsCliService
{
    private const int MaxOutputPreviewLength = 1000;
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

    public async Task<Result<CommandRunDto>> RunCommandAsync(
        Guid userId,
        RunCommandDto dto,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.TemplateName))
                return Result.Failure<CommandRunDto>(ErrorCodes.ValidationError, "TemplateName is required");
            if (!_templates.TryGetValue(dto.TemplateName, out var template))
                return Result.Failure<CommandRunDto>(ErrorCodes.ValidationError, $"Unknown command template: {dto.TemplateName}");

            var parameterValidation = ValidateTemplateParameters(template, dto.Parameters);
            if (!parameterValidation.IsSuccess)
                return Result.Failure<CommandRunDto>(parameterValidation.ErrorCode, parameterValidation.ErrorMessage);

            var user = await _unitOfWork.Users.GetByIdAsync(userId, ct);
            if (user == null)
                return Result.Failure<CommandRunDto>(ErrorCodes.NotFound, $"User with ID {userId} was not found");
            if (!HasRequiredRole(user.DefaultRole, template.RequiredRole))
            {
                var runnableTemplates = GetRunnableTemplatesForRole(user.DefaultRole)
                    .Select(commandTemplate => commandTemplate.Name)
                    .ToList();

                var runnableTemplateList = runnableTemplates.Count == 0
                    ? "none"
                    : string.Join(", ", runnableTemplates);

                return Result.Failure<CommandRunDto>(
                    ErrorCodes.Forbidden,
                    $"Template '{template.Name}' requires role '{template.RequiredRole}'. " +
                    $"Your current role is '{user.DefaultRole.ToString().ToLowerInvariant()}'. " +
                    $"Runnable templates for your role: {runnableTemplateList}. " +
                    "Next step: open Workspace > Settings to confirm your account role, then ask an owner/admin to assign elevated access if needed.");
            }

            var effectiveCorrelationId = string.IsNullOrWhiteSpace(correlationId)
                ? Guid.NewGuid().ToString("N")
                : correlationId;

            var commandRun = new CommandRun(dto.TemplateName, userId, effectiveCorrelationId);
            await _unitOfWork.CommandRuns.AddAsync(commandRun, ct);

            commandRun.Start();
            commandRun.AddLog(new CommandRunLog(
                commandRun.Id,
                "Info",
                "OpsCliService",
                $"Starting template '{dto.TemplateName}'",
                metadata: dto.Parameters == null ? null : System.Text.Json.JsonSerializer.Serialize(dto.Parameters)));
            await _unitOfWork.SaveChangesAsync(ct);

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(template.TimeoutSeconds));

                var output = await ExecuteTemplateAsync(dto.TemplateName, dto.Parameters, timeoutCts.Token);
                var redactedOutput = RedactSensitiveContent(output);

                if (redactedOutput.Length > MaxOutputPreviewLength)
                {
                    commandRun.SetOutputPreview(redactedOutput[..MaxOutputPreviewLength]);
                    commandRun.SetTruncated();
                }
                else
                {
                    commandRun.SetOutputPreview(redactedOutput);
                }

                commandRun.AddLog(new CommandRunLog(commandRun.Id, "Info", "OpsCliService", $"Command '{dto.TemplateName}' completed successfully"));
                commandRun.Complete(0);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                commandRun.AddLog(new CommandRunLog(commandRun.Id, "Error", "OpsCliService", $"Command '{dto.TemplateName}' timed out"));
                commandRun.Timeout();
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

    private static IEnumerable<CommandTemplateDto> GetRunnableTemplatesForRole(UserRole userRole)
    {
        return _templates.Values
            .Where(template => HasRequiredRole(userRole, template.RequiredRole))
            .OrderBy(template => template.Name);
    }

    private async Task<string> ExecuteTemplateAsync(string templateName, Dictionary<string, string>? parameters, CancellationToken ct)
    {
        return templateName switch
        {
            "boards.list" => await ExecuteBoardsListAsync(ct),
            "boards.search" => await ExecuteBoardsSearchAsync(parameters, ct),
            "queue.stats" => await ExecuteQueueStatsAsync(ct),
            "queue.pending" => await ExecuteQueuePendingAsync(ct),
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

    private async Task<string> ExecuteQueueStatsAsync(CancellationToken ct)
    {
        var allItems = await _unitOfWork.LlmQueue.GetAllAsync(ct);
        var grouped = allItems.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count());
        var pending = grouped.GetValueOrDefault(Domain.Enums.RequestStatus.Pending);
        var processing = grouped.GetValueOrDefault(Domain.Enums.RequestStatus.Processing);
        var completed = grouped.GetValueOrDefault(Domain.Enums.RequestStatus.Completed);
        var failed = grouped.GetValueOrDefault(Domain.Enums.RequestStatus.Failed);
        return $"Queue stats: Pending={pending}, Processing={processing}, Completed={completed}, Failed={failed}";
    }

    private async Task<string> ExecuteQueuePendingAsync(CancellationToken ct)
    {
        var pending = await _unitOfWork.LlmQueue.GetByStatusAsync(Domain.Enums.RequestStatus.Pending, ct);
        var pendingList = pending.Take(50).ToList();
        return $"Pending queue items: {pendingList.Count}\n" + string.Join("\n", pendingList.Select(r => $"- {r.Id}: {r.RequestType} (created: {r.CreatedAt})"));
    }

    private Result ValidateTemplateParameters(CommandTemplateDto template, Dictionary<string, string>? parameters)
    {
        var providedParameters = parameters ?? new Dictionary<string, string>();

        var unknownParameters = providedParameters.Keys
            .Where(key => !template.AcceptedParameters.Contains(key, StringComparer.Ordinal))
            .ToList();
        if (unknownParameters.Count > 0)
        {
            return Result.Failure(
                ErrorCodes.ValidationError,
                $"Unsupported parameter(s) for template '{template.Name}': {string.Join(", ", unknownParameters)}");
        }

        var missingParameters = template.AcceptedParameters
            .Where(name => !providedParameters.ContainsKey(name))
            .ToList();
        if (missingParameters.Count > 0)
        {
            return Result.Failure(
                ErrorCodes.ValidationError,
                $"Missing required parameter(s): {string.Join(", ", missingParameters)}");
        }

        return Result.Success();
    }

    private static bool HasRequiredRole(UserRole userRole, string requiredRole)
    {
        return TryParseRole(requiredRole, out var required) && (int)userRole <= (int)required;
    }

    private static bool TryParseRole(string role, out UserRole parsedRole)
    {
        return Enum.TryParse(role, ignoreCase: true, out parsedRole);
    }

    private static string RedactSensitiveContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        var result = content;
        var redactionPatterns = new[]
        {
            "Bearer ",
            "token=",
            "password=",
            "secret="
        };

        foreach (var pattern in redactionPatterns)
        {
            var index = result.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                var start = index + pattern.Length;
                var end = result.IndexOfAny(new[] { ' ', '\n', '\r', '\t', ',' }, start);
                if (end < 0)
                {
                    end = result.Length;
                }

                result = result[..start] + "***" + result[end..];
                index = result.IndexOf(pattern, start + 3, StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
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
