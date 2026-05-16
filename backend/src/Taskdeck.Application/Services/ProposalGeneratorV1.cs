using Microsoft.Extensions.Logging;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class ProposalGeneratorV1 : IProposalGenerator
{
    private readonly IDeterministicPreExtractor _preExtractor;
    private readonly IFieldVerifier _fieldVerifier;
    private readonly IProposalProvenanceRepository _provenanceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ProposalGeneratorV1> _logger;

    private const string ModelId = "proposal-generator-v1";

    public ProposalGeneratorV1(
        IDeterministicPreExtractor preExtractor,
        IFieldVerifier fieldVerifier,
        IProposalProvenanceRepository provenanceRepository,
        IUnitOfWork unitOfWork,
        ILogger<ProposalGeneratorV1> logger)
    {
        _preExtractor = preExtractor ?? throw new ArgumentNullException(nameof(preExtractor));
        _fieldVerifier = fieldVerifier ?? throw new ArgumentNullException(nameof(fieldVerifier));
        _provenanceRepository = provenanceRepository ?? throw new ArgumentNullException(nameof(provenanceRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<ProposalGenerationResult>> GenerateAsync(
        IntentEnvelopeV1 envelope,
        Guid boardId,
        CancellationToken cancellationToken = default)
    {
        if (envelope is null)
            return Result.Failure<ProposalGenerationResult>(ErrorCodes.ValidationError, "Envelope cannot be null");
        if (envelope.Status != EnvelopeStatus.Extracting)
            return Result.Failure<ProposalGenerationResult>(ErrorCodes.InvalidOperation,
                $"Envelope must be in Extracting status, but is {envelope.Status}");
        if (envelope.IntentCandidates.Count == 0)
            return Result.Failure<ProposalGenerationResult>(ErrorCodes.ValidationError,
                "Envelope must have at least one intent candidate");

        try
        {
            var sourceText = BuildSourceText(envelope.SourceBlocks);
            var preExtracted = _preExtractor.Extract(sourceText);

            var batch = envelope.CreateBatch(envelope.UserId, BuildBatchSummary(envelope), schemaVersion: 1);
            var generatedProposals = new List<GeneratedProposal>();

            foreach (var intent in envelope.IntentCandidates.OrderBy(c => c.Rank))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var proposal = CreateProposalFromIntent(intent, envelope, boardId);
                await _unitOfWork.AutomationProposals.AddAsync(proposal, cancellationToken);
                batch.AddProposalId(proposal.Id);

                var (provenance, verificationResults) = BuildProvenance(
                    proposal.Id, intent, envelope, sourceText, preExtracted);
                await _provenanceRepository.AddAsync(provenance, cancellationToken);

                generatedProposals.Add(new GeneratedProposal(
                    proposal.Id,
                    intent.Label,
                    intent.ActionType ?? "unknown",
                    provenance,
                    verificationResults));
            }

            if (generatedProposals.Count == 0)
            {
                batch.Discard();
                return Result.Success(new ProposalGenerationResult(
                    batch, generatedProposals, ModelId, totalTokens: 0));
            }

            batch.Seal();
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Generated {Count} proposals from envelope {EnvelopeId} for board {BoardId}",
                generatedProposals.Count, envelope.Id, boardId);

            return Result.Success(new ProposalGenerationResult(
                batch, generatedProposals, ModelId, totalTokens: 0));
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Domain error during proposal generation for envelope {EnvelopeId}", envelope.Id);
            return Result.Failure<ProposalGenerationResult>(ex.ErrorCode, ex.Message);
        }
    }

    private static string BuildSourceText(IReadOnlyList<SourceBlock> sourceBlocks)
    {
        if (sourceBlocks.Count == 0)
            return string.Empty;
        if (sourceBlocks.Count == 1)
            return sourceBlocks[0].Content;

        return string.Join("\n", sourceBlocks.OrderBy(b => b.Position).Select(b => b.Content));
    }

    private static string BuildBatchSummary(IntentEnvelopeV1 envelope)
    {
        var intentCount = envelope.IntentCandidates.Count;
        var topIntent = envelope.IntentCandidates.OrderBy(c => c.Rank).First();
        return intentCount == 1
            ? topIntent.Label
            : $"{topIntent.Label} (+{intentCount - 1} more)";
    }

    private static AutomationProposal CreateProposalFromIntent(
        IntentCandidate intent,
        IntentEnvelopeV1 envelope,
        Guid boardId)
    {
        var riskLevel = ClassifyRisk(intent);

        var proposal = new AutomationProposal(
            ProposalSourceType.Queue,
            envelope.UserId,
            TruncateSummary(intent.Label),
            riskLevel,
            envelope.Id.ToString(),
            boardId,
            envelope.CorrelationId);

        var operation = new AutomationProposalOperation(
            proposal.Id,
            sequence: 0,
            actionType: intent.ActionType ?? "unknown",
            targetType: "card",
            parameters: "{}",
            idempotencyKey: $"{envelope.Id}:{intent.Id}");

        proposal.AddOperation(operation);
        return proposal;
    }

    private static RiskLevel ClassifyRisk(IntentCandidate intent)
    {
        var actionType = intent.ActionType?.ToLowerInvariant() ?? "";

        if (actionType.Contains("delete") || actionType.Contains("archive"))
            return RiskLevel.High;
        if (actionType.Contains("move") || actionType.Contains("update"))
            return RiskLevel.Medium;

        return RiskLevel.Low;
    }

    private static string TruncateSummary(string label)
    {
        return label.Length <= 500 ? label : label[..497] + "...";
    }

    private (ProposalProvenance provenance, IReadOnlyList<FieldVerificationResult> results) BuildProvenance(
        Guid proposalId,
        IntentCandidate intent,
        IntentEnvelopeV1 envelope,
        string sourceText,
        IReadOnlyList<ExtractedEntity> preExtracted)
    {
        var correlationId = $"{envelope.Id}:{intent.Id}";
        var provenance = new ProposalProvenance(proposalId, correlationId, ModelId, totalTokens: 0);
        var verificationResults = new List<FieldVerificationResult>();

        var labelField = new ProvenanceField(
            "Label",
            ProvenanceKind.Extractive,
            intent.Confidence,
            provenance.Id,
            extractiveQuote: intent.Label);
        provenance.AddField(labelField);

        var labelVerification = _fieldVerifier.VerifyExtractiveField(
            "Label", intent.Label, sourceText, intent.Confidence);
        verificationResults.Add(labelVerification);

        ApplyVerificationToField(labelField, labelVerification);

        var actionTypeField = new ProvenanceField(
            "ActionType",
            ProvenanceKind.Inferred,
            intent.Confidence,
            provenance.Id);
        provenance.AddField(actionTypeField);

        var evidenceLinks = BuildEvidenceLinks(actionTypeField, intent, envelope);
        var actionVerification = _fieldVerifier.VerifyInferredField(
            "ActionType", evidenceLinks, envelope.SourceBlocks, intent.Confidence);
        verificationResults.Add(actionVerification);

        ApplyVerificationToField(actionTypeField, actionVerification);

        AddPreExtractedFields(provenance, preExtracted, sourceText, verificationResults);

        return (provenance, verificationResults);
    }

    private static void ApplyVerificationToField(ProvenanceField field, FieldVerificationResult verification)
    {
        if (verification.Status is VerificationStatus.Downgraded or VerificationStatus.Failed
            && verification.AdjustedConfidence < field.Confidence)
        {
            field.DowngradeConfidence(verification.AdjustedConfidence);
        }
    }

    private static IReadOnlyList<ProvenanceEvidenceLink> BuildEvidenceLinks(
        ProvenanceField field,
        IntentCandidate intent,
        IntentEnvelopeV1 envelope)
    {
        var links = new List<ProvenanceEvidenceLink>();

        foreach (var evidenceLink in intent.EvidenceLinks)
        {
            var sourceSpan = envelope.SourceBlocks
                .SelectMany(b => b.Spans)
                .FirstOrDefault(s => s.Id == evidenceLink.SourceSpanId);

            if (sourceSpan == null)
                continue;

            var sourceBlock = envelope.SourceBlocks.FirstOrDefault(b => b.Id == sourceSpan.SourceBlockId);
            if (sourceBlock == null)
                continue;
            var provenanceLink = new ProvenanceEvidenceLink(
                sourceBlock.SourceType,
                sourceBlock.Id.ToString(),
                field.Id,
                label: evidenceLink.Rationale,
                spanStart: sourceSpan.StartOffset,
                spanEnd: sourceSpan.EndOffset);

            field.AddEvidenceLink(provenanceLink);
            links.Add(provenanceLink);
        }

        if (links.Count == 0 && envelope.SourceBlocks.Count > 0)
        {
            var firstBlock = envelope.SourceBlocks[0];
            var fallbackLink = new ProvenanceEvidenceLink(
                firstBlock.SourceType,
                firstBlock.Id.ToString(),
                field.Id,
                label: "Inferred from source context");
            field.AddEvidenceLink(fallbackLink);
            links.Add(fallbackLink);
        }

        return links;
    }

    private void AddPreExtractedFields(
        ProposalProvenance provenance,
        IReadOnlyList<ExtractedEntity> preExtracted,
        string sourceText,
        List<FieldVerificationResult> verificationResults)
    {
        foreach (var entity in preExtracted.Take(10))
        {
            var rawFieldName = $"PreExtracted:{entity.EntityType}";
            var fieldName = rawFieldName.Length <= 100 ? rawFieldName : rawFieldName[..100];
            var field = new ProvenanceField(
                fieldName,
                ProvenanceKind.Extractive,
                confidence: 1.0,
                provenance.Id,
                extractiveQuote: entity.Text);
            provenance.AddField(field);

            var verification = _fieldVerifier.VerifyExtractiveField(
                fieldName, entity.Text, sourceText, 1.0);
            verificationResults.Add(verification);
        }
    }
}
