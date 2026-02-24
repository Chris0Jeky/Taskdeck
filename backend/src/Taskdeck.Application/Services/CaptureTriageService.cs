using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class CaptureTriageService : ICaptureTriageService
{
    private const int MaxExtractedTasks = CaptureTriageOutputContract.MaxTasks;

    private static readonly Regex ChecklistPattern = new(
        @"^\s*[-*]\s+\[[xX ]\]\s+(.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex BulletPattern = new(
        @"^\s*[-*\u2022]\s+(.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex NumberedPattern = new(
        @"^\s*\d+[.)]\s+(.+?)\s*$",
        RegexOptions.Compiled);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAutomationProposalService _proposalService;
    private readonly IAutomationPolicyEngine _policyEngine;
    private readonly ILlmProvider _llmProvider;
    private readonly ILogger<CaptureTriageService>? _logger;

    public CaptureTriageService(
        IUnitOfWork unitOfWork,
        IAutomationProposalService proposalService,
        IAutomationPolicyEngine policyEngine,
        ILlmProvider llmProvider,
        ILogger<CaptureTriageService>? logger = null)
    {
        _unitOfWork = unitOfWork;
        _proposalService = proposalService;
        _policyEngine = policyEngine;
        _llmProvider = llmProvider;
        _logger = logger;
    }

    public async Task<Result<CaptureTriageProposalResultDto>> CreateProposalFromCaptureAsync(
        Guid captureItemId,
        Guid userId,
        Guid? boardId,
        CapturePayloadV1 payload,
        CancellationToken cancellationToken = default)
    {
        if (captureItemId == Guid.Empty)
            return Result.Failure<CaptureTriageProposalResultDto>(ErrorCodes.ValidationError, "CaptureItemId cannot be empty");

        if (userId == Guid.Empty)
            return Result.Failure<CaptureTriageProposalResultDto>(ErrorCodes.ValidationError, "UserId cannot be empty");

        if (!boardId.HasValue)
        {
            return Result.Failure<CaptureTriageProposalResultDto>(
                ErrorCodes.ValidationError,
                "BoardId is required to triage capture items into proposals");
        }

        var board = await _unitOfWork.Boards.GetByIdAsync(boardId.Value, cancellationToken);
        if (board == null)
        {
            return Result.Failure<CaptureTriageProposalResultDto>(
                ErrorCodes.NotFound,
                $"Board with ID {boardId.Value} not found");
        }

        var columns = (await _unitOfWork.Columns.GetByBoardIdAsync(boardId.Value, cancellationToken))
            .OrderBy(column => column.Position)
            .ToList();
        var defaultColumn = columns.FirstOrDefault();
        if (defaultColumn == null)
        {
            return Result.Failure<CaptureTriageProposalResultDto>(
                ErrorCodes.NotFound,
                "No columns found in board");
        }

        var taskCandidates = ExtractTaskCandidates(payload.Text);
        if (taskCandidates.Count == 0)
        {
            return Result.Failure<CaptureTriageProposalResultDto>(
                ErrorCodes.ValidationError,
                "Capture text did not produce actionable triage items");
        }

        var outputModel = BuildOutputModel(taskCandidates);
        var outputValidation = CaptureTriageOutputContract.Validate(outputModel);
        if (!outputValidation.IsSuccess)
        {
            return Result.Failure<CaptureTriageProposalResultDto>(
                outputValidation.ErrorCode,
                outputValidation.ErrorMessage);
        }

        var operations = outputValidation.Value.Tasks
            .Select((task, sequence) => BuildCreateCardOperation(
                captureItemId,
                boardId.Value,
                defaultColumn.Id,
                task,
                sequence))
            .ToList();

        var operationDtos = operations
            .Select((operation, sequence) => new ProposalOperationDto(
                Guid.Empty,
                Guid.Empty,
                sequence,
                operation.ActionType,
                operation.TargetType,
                operation.TargetId,
                operation.Parameters,
                operation.IdempotencyKey,
                operation.ExpectedVersion))
            .ToList();

        var riskLevel = _policyEngine.ClassifyRisk(operationDtos);
        var triageRunId = Guid.NewGuid();
        var summary = BuildSummary(outputValidation.Value.Tasks);
        var permissionResult = await _policyEngine.ValidatePermissionsAsync(
            userId,
            boardId,
            operationDtos,
            cancellationToken);
        if (!permissionResult.IsSuccess)
        {
            return Result.Failure<CaptureTriageProposalResultDto>(
                permissionResult.ErrorCode,
                permissionResult.ErrorMessage);
        }

        var createProposalResult = await _proposalService.CreateProposalAsync(
            new CreateProposalDto(
                SourceType: ProposalSourceType.Queue,
                RequestedByUserId: userId,
                Summary: summary,
                RiskLevel: riskLevel,
                CorrelationId: triageRunId.ToString(),
                BoardId: boardId.Value,
                SourceReferenceId: captureItemId.ToString(),
                Operations: operations),
            cancellationToken);

        if (!createProposalResult.IsSuccess)
        {
            return Result.Failure<CaptureTriageProposalResultDto>(
                createProposalResult.ErrorCode,
                createProposalResult.ErrorMessage);
        }

        var (provider, model) = await GetProviderMetadataAsync(cancellationToken);

        return Result.Success(new CaptureTriageProposalResultDto(
            captureItemId,
            triageRunId,
            createProposalResult.Value.Id,
            operations.Count,
            outputValidation.Value.PromptVersion,
            provider,
            model));
    }

    private async Task<(string Provider, string Model)> GetProviderMetadataAsync(CancellationToken ct)
    {
        try
        {
            var health = await _llmProvider.GetHealthAsync(ct);
            var provider = CaptureRequestContract.SanitizeProvenanceMetadata(
                health.ProviderName,
                CaptureRequestContract.MaxProviderLength);
            var model = CaptureRequestContract.SanitizeProvenanceMetadata(
                health.Model,
                CaptureRequestContract.MaxModelLength);
            return (provider, model);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to resolve LLM provider metadata for capture triage provenance. Falling back to unknown values.");
            return ("unknown", "unknown");
        }
    }

    private static List<string> ExtractTaskCandidates(string rawText)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var lines = rawText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var extracted = TryExtractStructuredTask(line);
            if (string.IsNullOrWhiteSpace(extracted))
            {
                continue;
            }

            var normalized = NormalizeTaskTitle(extracted);
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
            {
                continue;
            }

            candidates.Add(normalized);
            if (candidates.Count >= MaxExtractedTasks)
            {
                return candidates;
            }
        }

        if (candidates.Count > 0)
        {
            return candidates;
        }

        var fallback = NormalizeTaskTitle(rawText);
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            candidates.Add(fallback);
        }

        return candidates;
    }

    private static string? TryExtractStructuredTask(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var checklistMatch = ChecklistPattern.Match(line);
        if (checklistMatch.Success)
        {
            return checklistMatch.Groups[1].Value;
        }

        var numberedMatch = NumberedPattern.Match(line);
        if (numberedMatch.Success)
        {
            return numberedMatch.Groups[1].Value;
        }

        var bulletMatch = BulletPattern.Match(line);
        if (bulletMatch.Success)
        {
            return bulletMatch.Groups[1].Value;
        }

        return null;
    }

    private static string NormalizeTaskTitle(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var sentenceSplit = Regex.Split(trimmed, @"(?<=[.!?])\s+");
        if (sentenceSplit.Length > 0 && sentenceSplit[0].Length >= 8)
        {
            trimmed = sentenceSplit[0];
        }

        var normalized = Regex.Replace(trimmed, @"\s+", " ").Trim();
        if (normalized.Length > CaptureTriageOutputContract.MaxTaskTitleLength)
        {
            normalized = normalized[..CaptureTriageOutputContract.MaxTaskTitleLength].TrimEnd();
        }

        return normalized;
    }

    private static CreateProposalOperationDto BuildCreateCardOperation(
        Guid captureItemId,
        Guid boardId,
        Guid columnId,
        CaptureTriageTaskV1 task,
        int sequence)
    {
        var cardId = BuildDeterministicCardId(captureItemId, sequence, task.Title);
        var parameters = JsonSerializer.Serialize(new
        {
            title = task.Title,
            description = task.Evidence,
            columnId,
            boardId
        });

        return new CreateProposalOperationDto(
            Sequence: sequence,
            ActionType: "create",
            TargetType: "card",
            Parameters: parameters,
            IdempotencyKey: BuildIdempotencyKey(captureItemId, sequence, task.Title),
            TargetId: cardId.ToString());
    }

    private static string BuildIdempotencyKey(Guid captureItemId, int sequence, string taskTitle)
    {
        var normalizedTitle = Regex.Replace(taskTitle.ToLowerInvariant().Trim(), @"\s+", " ");
        var material = $"{captureItemId:N}:{sequence}:{normalizedTitle}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Guid BuildDeterministicCardId(Guid captureItemId, int sequence, string taskTitle)
    {
        var normalizedTitle = Regex.Replace(taskTitle.ToLowerInvariant().Trim(), @"\s+", " ");
        var material = $"capture-card:{captureItemId:N}:{sequence}:{normalizedTitle}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var bytes = hash.Take(16).ToArray();
        return new Guid(bytes);
    }

    private static CaptureTriageOutputV1 BuildOutputModel(IReadOnlyCollection<string> taskCandidates)
    {
        var tasks = taskCandidates
            .Take(CaptureTriageOutputContract.MaxTasks)
            .Select(task => new CaptureTriageTaskV1(task, task))
            .ToList();

        return new CaptureTriageOutputV1(
            CaptureTriageOutputContract.SchemaVersion,
            CaptureTriageOutputContract.PromptVersionV1,
            tasks);
    }

    private static string BuildSummary(IReadOnlyCollection<CaptureTriageTaskV1> tasks)
    {
        var lead = tasks.FirstOrDefault()?.Title ?? "capture triage";
        if (tasks.Count == 1)
        {
            return $"Capture triage: {lead}";
        }

        return $"Capture triage ({tasks.Count} tasks): {lead}";
    }
}
