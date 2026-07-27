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

    /// <summary>
    /// Provenance actor recorded when the deterministic text extractor
    /// (<see cref="ExtractTaskCandidates"/>) produced the triage output. It never invokes an LLM,
    /// so the recorded provider/model must name the extractor itself — not the workspace's
    /// configured live LLM provider. Recording the live provider here would be false provenance
    /// (#1273). When the LLM extraction leg (REVIVAL-08) produced the output, the real
    /// provider/model reported by <see cref="ILlmCaptureTriageExtractor"/> is recorded instead.
    /// </summary>
    public const string TriageProviderName = "deterministic-extractor";

    /// <summary>Model/version identifier for the deterministic capture-triage extractor (#1273).</summary>
    public const string TriageModelName = "capture-triage-v1";

    /// <summary>
    /// Provenance value recorded when the engine that produced an existing proposal cannot be
    /// determined (a prior run created the proposal but crashed before stamping the payload; on
    /// retry either engine could have been the author). Matches
    /// <see cref="CaptureRequestContract.SanitizeProvenanceMetadata"/>'s fallback so downstream
    /// consumers see one "we don't know" spelling. Naming a concrete engine here would risk false
    /// provenance (#1273).
    /// </summary>
    public const string UnknownProvenanceValue = "unknown";

    private static readonly Regex ChecklistPattern = new(
        @"^\s*[-*]\s+\[[xX ]\]\s+(.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex BulletPattern = new(
        @"^\s*[-*\u2022]\s+(.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex NumberedPattern = new(
        @"^\s*\d+[.)]\s+(.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex DashDelimiterPattern = new(
        @"[^\S\n]+-[^\S\n]+",
        RegexOptions.Compiled);

    private static readonly Regex SemicolonDelimiterPattern = new(
        @";\s+",
        RegexOptions.Compiled);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAutomationProposalService _proposalService;
    private readonly IAutomationPolicyEngine _policyEngine;
    private readonly ILlmCaptureTriageExtractor? _llmExtractor;
    private readonly ILogger<CaptureTriageService>? _logger;

    public CaptureTriageService(
        IUnitOfWork unitOfWork,
        IAutomationProposalService proposalService,
        IAutomationPolicyEngine policyEngine,
        ILlmCaptureTriageExtractor? llmExtractor = null,
        ILogger<CaptureTriageService>? logger = null)
    {
        _unitOfWork = unitOfWork;
        _proposalService = proposalService;
        _policyEngine = policyEngine;
        _llmExtractor = llmExtractor;
        _logger = logger;
    }

    public async Task<Result<CaptureTriageProposalResultDto>> CreateProposalFromCaptureAsync(
        Guid captureItemId,
        Guid userId,
        Guid? boardId,
        CapturePayloadV1 payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (payload is null)
            return Result.Failure<CaptureTriageProposalResultDto>(ErrorCodes.ValidationError, "Capture payload cannot be null");

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

        var captureReferenceId = captureItemId.ToString();
        var llmIsCandidate = _llmExtractor is not null &&
                             CaptureRequestContract.IsTranscriptSource(payload.Source);

        // Replay idempotency comes FIRST: a prior attempt may have committed the proposal and then
        // crashed before the worker stamped the payload. The reuse short-circuit must run before
        // any validation that can legitimately fail on replay (board deleted, columns removed,
        // permission revoked since the original run) — otherwise the replay fails permanently and
        // orphans the committed proposal — and before any LLM spend, so a retry never burns a
        // second extraction call for output the reuse would discard.
        var existingProposal = await _unitOfWork.AutomationProposals.GetBySourceReferenceAsync(
            ProposalSourceType.Queue,
            captureReferenceId,
            cancellationToken);
        if (existingProposal != null)
        {
            var (reuseProvider, reuseModel, reusePromptVersion) = ResolveReuseProvenance(
                payload,
                llmIsCandidate,
                TriageProviderName,
                TriageModelName,
                CaptureTriageOutputContract.PromptVersionV1);
            return Result.Success(new CaptureTriageProposalResultDto(
                captureItemId,
                ResolveTriageRunId(existingProposal.CorrelationId, Guid.NewGuid()),
                existingProposal.Id,
                existingProposal.Operations.Count,
                reusePromptVersion,
                reuseProvider,
                reuseModel));
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

        CaptureTriageOutputV1? outputModel = null;
        var triageProvider = TriageProviderName;
        var triageModel = TriageModelName;

        if (llmIsCandidate)
        {
            var extraction = await RunLlmExtractionLegAsync(captureItemId, userId, boardId, payload, cancellationToken);
            if (extraction.Succeeded)
            {
                outputModel = extraction.Output;
                triageProvider = CaptureRequestContract.SanitizeProvenanceMetadata(
                    extraction.Provider, CaptureRequestContract.MaxProviderLength);
                triageModel = CaptureRequestContract.SanitizeProvenanceMetadata(
                    extraction.Model, CaptureRequestContract.MaxModelLength);
            }
            else if (extraction.Outcome == LlmCaptureTriageOutcome.EmptyExtraction)
            {
                // A deliberate zero-item verdict from a successful LLM run. Degrading to the
                // deterministic extractor would fabricate a card out of unactionable text (its
                // whole-text fallback always yields one), and reporting failure would surface a
                // correct extraction as a red Failed capture and invite a pointless retry loop.
                // Return the "triaged, nothing to propose" success shape instead: no ProposalId,
                // zero operations, provenance naming the engine that actually ran. The workers map
                // this to Completed-without-proposal, which the capture status policy already
                // renders as the terminal Triaged state.
                return Result.Success(new CaptureTriageProposalResultDto(
                    captureItemId,
                    Guid.NewGuid(),
                    ProposalId: null,
                    OperationCount: 0,
                    CaptureRequestContract.SanitizeProvenanceMetadata(
                        extraction.PromptVersion, CaptureRequestContract.MaxPromptVersionLength),
                    CaptureRequestContract.SanitizeProvenanceMetadata(
                        extraction.Provider, CaptureRequestContract.MaxProviderLength),
                    CaptureRequestContract.SanitizeProvenanceMetadata(
                        extraction.Model, CaptureRequestContract.MaxModelLength)));
            }
            else
            {
                _logger?.LogInformation(
                    "LLM transcript triage unavailable for capture {CaptureItemId} ({Outcome}); using deterministic extractor. {Detail}",
                    captureItemId,
                    extraction.Outcome,
                    extraction.Detail ?? string.Empty);
            }
        }

        if (outputModel is null)
        {
            var (taskCandidates, contextHint) = ExtractTaskCandidates(payload.Text);
            if (taskCandidates.Count == 0)
            {
                return Result.Failure<CaptureTriageProposalResultDto>(
                    ErrorCodes.ValidationError,
                    "Capture text did not produce actionable triage items");
            }

            outputModel = BuildOutputModel(taskCandidates, contextHint);
        }

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
                SourceReferenceId: captureReferenceId,
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
            operations.Count,
            outputValidation.Value.PromptVersion,
            triageProvider,
            triageModel));
    }

    /// <summary>
    /// Runs the LLM extraction leg, converting unexpected exceptions into a fallback-triggering
    /// outcome: an LLM-side problem must degrade to the deterministic extractor, never fail the
    /// capture item (the extractor contract already reports expected failures as outcomes; this
    /// guard covers bugs and infrastructure surprises).
    /// </summary>
    private async Task<LlmCaptureTriageExtraction> RunLlmExtractionLegAsync(
        Guid captureItemId,
        Guid userId,
        Guid? boardId,
        CapturePayloadV1 payload,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _llmExtractor!.ExtractAsync(userId, boardId, payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "LLM transcript triage threw unexpectedly for capture {CaptureItemId}; using deterministic extractor",
                captureItemId);
            return new LlmCaptureTriageExtraction(
                LlmCaptureTriageOutcome.InvalidOutput,
                Detail: ex.Message);
        }
    }

    /// <summary>
    /// Resolves the provenance to report when an existing proposal is reused instead of creating a
    /// new one. The current run did not author that proposal, so naming the current engine could be
    /// false provenance (#1273): a crashed LLM run may have authored it, then a retry lands here
    /// after the LLM leg was skipped or fell back. The payload's stamped provenance (the author's
    /// own record) wins when present; otherwise, if the LLM could have authored it the honest value
    /// is "unknown"; only when the deterministic extractor is the sole possible author are its
    /// constants safe to report.
    /// </summary>
    private static (string Provider, string Model, string PromptVersion) ResolveReuseProvenance(
        CapturePayloadV1 payload,
        bool llmIsCandidate,
        string currentProvider,
        string currentModel,
        string currentPromptVersion)
    {
        var stamped = payload.Provenance;
        if (!string.IsNullOrWhiteSpace(stamped?.Provider) &&
            !string.IsNullOrWhiteSpace(stamped?.Model) &&
            !string.IsNullOrWhiteSpace(stamped?.PromptVersion))
        {
            return (stamped!.Provider!, stamped.Model!, stamped.PromptVersion!);
        }

        if (llmIsCandidate)
        {
            return (UnknownProvenanceValue, UnknownProvenanceValue, UnknownProvenanceValue);
        }

        return (currentProvider, currentModel, currentPromptVersion);
    }

    private static Guid ResolveTriageRunId(string? correlationId, Guid fallback)
    {
        return Guid.TryParse(correlationId, out var parsed) && parsed != Guid.Empty
            ? parsed
            : fallback;
    }

    private static (List<string> Tasks, string? ContextHint) ExtractTaskCandidates(string rawText)
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
                return (candidates, null);
            }
        }

        if (candidates.Count > 0)
        {
            return (candidates, null);
        }

        // Try dash-separated: first segment is context hint, rest are tasks
        var dashSegments = DashDelimiterPattern.Split(rawText)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (dashSegments.Count >= 3)
        {
            var contextHint = NormalizeTaskTitle(dashSegments[0]);
            foreach (var segment in dashSegments.Skip(1))
            {
                var normalized = NormalizeTaskTitle(segment);
                if (!string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized))
                {
                    candidates.Add(normalized);
                    if (candidates.Count >= MaxExtractedTasks)
                    {
                        return (candidates, contextHint);
                    }
                }
            }

            if (candidates.Count > 0)
            {
                return (candidates, contextHint);
            }
        }

        // Try semicolons: all segments are equal tasks
        var semicolonSegments = SemicolonDelimiterPattern.Split(rawText)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (semicolonSegments.Count >= 2)
        {
            foreach (var segment in semicolonSegments)
            {
                var normalized = NormalizeTaskTitle(segment);
                if (!string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized))
                {
                    candidates.Add(normalized);
                    if (candidates.Count >= MaxExtractedTasks)
                    {
                        return (candidates, null);
                    }
                }
            }

            if (candidates.Count > 0)
            {
                return (candidates, null);
            }
        }

        // Single-sentence fallback: create one card with the full text
        var fallback = NormalizeTaskTitle(rawText);
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            candidates.Add(fallback);
        }

        return (candidates, null);
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
        normalized = CaptureTriageOutputContract.SanitizeTaskTitle(normalized);
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

    private static CaptureTriageOutputV1 BuildOutputModel(
        IReadOnlyCollection<string> taskCandidates,
        string? contextHint = null)
    {
        var hasContext = !string.IsNullOrWhiteSpace(contextHint);
        var tasks = taskCandidates
            .Take(CaptureTriageOutputContract.MaxTasks)
            .Select(task =>
            {
                var evidence = hasContext ? $"{contextHint}: {task}" : task;
                if (evidence.Length > CaptureTriageOutputContract.MaxTaskEvidenceLength)
                {
                    evidence = evidence[..CaptureTriageOutputContract.MaxTaskEvidenceLength].TrimEnd();
                }

                return new CaptureTriageTaskV1(task, evidence);
            })
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
