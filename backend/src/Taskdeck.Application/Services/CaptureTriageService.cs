using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class CaptureTriageService : ICaptureTriageService
{
    private const int MaxExtractedTasks = 20;
    private const int MaxTaskTitleLength = 180;

    private static readonly Regex ChecklistPattern = new(
        @"^\s*[-*]\s+\[[xX ]\]\s+(.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex BulletPattern = new(
        @"^\s*[-*•]\s+(.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex NumberedPattern = new(
        @"^\s*\d+[.)]\s+(.+?)\s*$",
        RegexOptions.Compiled);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAutomationProposalService _proposalService;
    private readonly IAutomationPolicyEngine _policyEngine;

    public CaptureTriageService(
        IUnitOfWork unitOfWork,
        IAutomationProposalService proposalService,
        IAutomationPolicyEngine policyEngine)
    {
        _unitOfWork = unitOfWork;
        _proposalService = proposalService;
        _policyEngine = policyEngine;
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

        var operations = taskCandidates
            .Select((taskTitle, sequence) => BuildCreateCardOperation(
                captureItemId,
                boardId.Value,
                defaultColumn.Id,
                taskTitle,
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
        var summary = BuildSummary(taskCandidates);

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

        return Result.Success(new CaptureTriageProposalResultDto(
            captureItemId,
            triageRunId,
            createProposalResult.Value.Id,
            operations.Count));
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
        if (normalized.Length > MaxTaskTitleLength)
        {
            normalized = normalized[..MaxTaskTitleLength].TrimEnd();
        }

        return normalized;
    }

    private static CreateProposalOperationDto BuildCreateCardOperation(
        Guid captureItemId,
        Guid boardId,
        Guid columnId,
        string taskTitle,
        int sequence)
    {
        var parameters = JsonSerializer.Serialize(new
        {
            title = taskTitle,
            columnId,
            boardId
        });

        return new CreateProposalOperationDto(
            Sequence: sequence,
            ActionType: "create",
            TargetType: "card",
            Parameters: parameters,
            IdempotencyKey: BuildIdempotencyKey(captureItemId, sequence, taskTitle));
    }

    private static string BuildIdempotencyKey(Guid captureItemId, int sequence, string taskTitle)
    {
        var normalizedTitle = Regex.Replace(taskTitle.ToLowerInvariant().Trim(), @"\s+", " ");
        var material = $"{captureItemId:N}:{sequence}:{normalizedTitle}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildSummary(IReadOnlyCollection<string> taskCandidates)
    {
        var lead = taskCandidates.FirstOrDefault() ?? "capture triage";
        if (taskCandidates.Count == 1)
        {
            return $"Capture triage: {lead}";
        }

        return $"Capture triage ({taskCandidates.Count} tasks): {lead}";
    }
}
