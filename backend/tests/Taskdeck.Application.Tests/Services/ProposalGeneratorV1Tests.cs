using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ProposalGeneratorV1Tests
{
    private readonly Mock<IDeterministicPreExtractor> _preExtractor = new();
    private readonly Mock<IFieldVerifier> _fieldVerifier = new();
    private readonly Mock<IProposalProvenanceRepository> _provenanceRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAutomationProposalRepository> _proposalRepo = new();
    private readonly Mock<ILogger<ProposalGeneratorV1>> _logger = new();
    private readonly ProposalGeneratorV1 _sut;

    public ProposalGeneratorV1Tests()
    {
        _unitOfWork.Setup(u => u.AutomationProposals).Returns(_proposalRepo.Object);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _preExtractor.Setup(p => p.Extract(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new List<ExtractedEntity>());
        _fieldVerifier.Setup(v => v.VerifyExtractiveField(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>()))
            .Returns((string name, string quote, string source, double conf) =>
                new FieldVerificationResult(name, VerificationStatus.Verified, conf, conf, 0.9));
        _fieldVerifier.Setup(v => v.VerifyInferredField(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<ProvenanceEvidenceLink>>(),
                It.IsAny<IReadOnlyList<SourceBlock>>(), It.IsAny<double>()))
            .Returns((string name, IReadOnlyList<ProvenanceEvidenceLink> _, IReadOnlyList<SourceBlock> __, double conf) =>
                new FieldVerificationResult(name, VerificationStatus.Verified, conf, conf, 1.0));

        _sut = new ProposalGeneratorV1(
            _preExtractor.Object,
            _fieldVerifier.Object,
            _provenanceRepo.Object,
            _unitOfWork.Object,
            _logger.Object);
    }

    [Fact]
    public async Task GenerateAsync_ValidEnvelope_ReturnsSuccess()
    {
        var envelope = CreateValidEnvelope();
        var boardId = Guid.NewGuid();

        var result = await _sut.GenerateAsync(envelope, boardId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Proposals.Should().HaveCount(1);
        result.Value.Batch.Should().NotBeNull();
        result.Value.ModelId.Should().Be("proposal-generator-v1");
    }

    [Fact]
    public async Task GenerateAsync_MultipleIntents_GeneratesMultipleProposals()
    {
        var envelope = CreateEnvelopeWithMultipleIntents();
        var boardId = Guid.NewGuid();

        var result = await _sut.GenerateAsync(envelope, boardId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Proposals.Should().HaveCount(2);
    }

    [Fact]
    public async Task GenerateAsync_NullEnvelope_ReturnsFailure()
    {
        var result = await _sut.GenerateAsync(null!, Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task GenerateAsync_WrongStatus_ReturnsFailure()
    {
        var envelope = new IntentEnvelopeV1("capture", "raw content", Guid.NewGuid());
        envelope.AddSourceBlock(0, "raw content", "capture");
        // Status is Created, not Extracting

        var result = await _sut.GenerateAsync(envelope, Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
    }

    [Fact]
    public async Task GenerateAsync_NoIntents_ReturnsFailure()
    {
        var userId = Guid.NewGuid();
        var envelope = new IntentEnvelopeV1("capture", "raw content", userId);
        envelope.AddSourceBlock(0, "raw content", "capture");
        // Force status to Extracting without adding intents via reflection
        SetStatus(envelope, EnvelopeStatus.Extracting);

        var result = await _sut.GenerateAsync(envelope, Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task GenerateAsync_PersistsProposalAndProvenance()
    {
        var envelope = CreateValidEnvelope();
        var boardId = Guid.NewGuid();

        await _sut.GenerateAsync(envelope, boardId);

        _proposalRepo.Verify(r => r.AddAsync(It.IsAny<AutomationProposal>(), It.IsAny<CancellationToken>()), Times.Once);
        _provenanceRepo.Verify(r => r.AddAsync(It.IsAny<ProposalProvenance>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_SealsBatch()
    {
        var envelope = CreateValidEnvelope();
        var boardId = Guid.NewGuid();

        var result = await _sut.GenerateAsync(envelope, boardId);

        result.Value.Batch.Status.Should().Be(ProposalBatchStatus.Sealed);
    }

    [Fact]
    public async Task GenerateAsync_CallsPreExtractor()
    {
        var envelope = CreateValidEnvelope();
        var boardId = Guid.NewGuid();

        await _sut.GenerateAsync(envelope, boardId);

        _preExtractor.Verify(p => p.Extract(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_CallsFieldVerifier()
    {
        var envelope = CreateValidEnvelope();
        var boardId = Guid.NewGuid();

        await _sut.GenerateAsync(envelope, boardId);

        _fieldVerifier.Verify(v => v.VerifyExtractiveField(
            "Label", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>()), Times.Once);
        _fieldVerifier.Verify(v => v.VerifyInferredField(
            "ActionType", It.IsAny<IReadOnlyList<ProvenanceEvidenceLink>>(),
            It.IsAny<IReadOnlyList<SourceBlock>>(), It.IsAny<double>()), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_IncludesPreExtractedFields()
    {
        _preExtractor.Setup(p => p.Extract(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new List<ExtractedEntity>
            {
                new("DateTime", "tomorrow", "2026-05-17", 0, 8),
                new("Url", "https://example.com", "https://example.com", 10, 29)
            });

        var envelope = CreateValidEnvelope();
        var boardId = Guid.NewGuid();

        var result = await _sut.GenerateAsync(envelope, boardId);

        var provenance = result.Value.Proposals[0].Provenance;
        provenance.Fields.Should().Contain(f => f.FieldName == "PreExtracted:DateTime");
        provenance.Fields.Should().Contain(f => f.FieldName == "PreExtracted:Url");
    }

    [Fact]
    public async Task GenerateAsync_DeleteIntent_ClassifiedAsHighRisk()
    {
        AutomationProposal? capturedProposal = null;
        _proposalRepo.Setup(r => r.AddAsync(It.IsAny<AutomationProposal>(), It.IsAny<CancellationToken>()))
            .Callback<AutomationProposal, CancellationToken>((p, _) => capturedProposal = p)
            .ReturnsAsync((AutomationProposal p, CancellationToken _) => p);

        var envelope = CreateEnvelopeWithActionType("delete-card");
        var boardId = Guid.NewGuid();

        var result = await _sut.GenerateAsync(envelope, boardId);

        result.IsSuccess.Should().BeTrue();
        capturedProposal.Should().NotBeNull();
        capturedProposal!.RiskLevel.Should().Be(RiskLevel.High);
    }

    [Fact]
    public async Task GenerateAsync_IntentsOrderedByRank()
    {
        var envelope = CreateEnvelopeWithMultipleIntents();
        var boardId = Guid.NewGuid();

        var result = await _sut.GenerateAsync(envelope, boardId);

        result.Value.Proposals[0].Summary.Should().Be("Create card for API");
        result.Value.Proposals[1].Summary.Should().Be("Move card to done");
    }

    [Fact]
    public async Task GenerateAsync_VerificationResultsIncluded()
    {
        var envelope = CreateValidEnvelope();
        var boardId = Guid.NewGuid();

        var result = await _sut.GenerateAsync(envelope, boardId);

        var proposal = result.Value.Proposals[0];
        proposal.VerificationResults.Should().HaveCountGreaterOrEqualTo(2);
        proposal.VerificationResults.Should().Contain(r => r.FieldName == "Label");
        proposal.VerificationResults.Should().Contain(r => r.FieldName == "ActionType");
    }

    [Fact]
    public async Task GenerateAsync_CancellationRespected()
    {
        var envelope = CreateEnvelopeWithMultipleIntents();
        var boardId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _sut.GenerateAsync(envelope, boardId, cts.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Proposals.Should().BeEmpty();
        result.Value.Batch.Status.Should().Be(ProposalBatchStatus.Discarded);
    }

    [Fact]
    public async Task GenerateAsync_PartialCancellation_ProducesPartialBatch()
    {
        var callCount = 0;
        var cts = new CancellationTokenSource();
        _proposalRepo.Setup(r => r.AddAsync(It.IsAny<AutomationProposal>(), It.IsAny<CancellationToken>()))
            .Callback<AutomationProposal, CancellationToken>((_, _) =>
            {
                callCount++;
                if (callCount >= 1) cts.Cancel();
            })
            .ReturnsAsync((AutomationProposal p, CancellationToken _) => p);

        var envelope = CreateEnvelopeWithMultipleIntents();
        var boardId = Guid.NewGuid();

        var result = await _sut.GenerateAsync(envelope, boardId, cts.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Proposals.Should().HaveCount(1);
        result.Value.Batch.Status.Should().Be(ProposalBatchStatus.Sealed);
    }

    [Fact]
    public async Task GenerateAsync_DowngradedVerification_AdjustsFieldConfidence()
    {
        _fieldVerifier.Setup(v => v.VerifyExtractiveField(
                "Label", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>()))
            .Returns(new FieldVerificationResult("Label", VerificationStatus.Downgraded, 0.8, 0.56, 0.7,
                "Partial match"));

        var envelope = CreateValidEnvelope();
        var boardId = Guid.NewGuid();

        var result = await _sut.GenerateAsync(envelope, boardId);

        result.IsSuccess.Should().BeTrue();
        var labelField = result.Value.Proposals[0].Provenance.Fields
            .First(f => f.FieldName == "Label");
        labelField.Confidence.Should().Be(0.56);
    }

    [Fact]
    public async Task GenerateAsync_FailedVerification_ZerosFieldConfidence()
    {
        _fieldVerifier.Setup(v => v.VerifyExtractiveField(
                "Label", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<double>()))
            .Returns(new FieldVerificationResult("Label", VerificationStatus.Failed, 0.85, 0.0, 0.2,
                "Quote not found"));

        var envelope = CreateValidEnvelope();
        var boardId = Guid.NewGuid();

        var result = await _sut.GenerateAsync(envelope, boardId);

        result.IsSuccess.Should().BeTrue();
        var labelField = result.Value.Proposals[0].Provenance.Fields
            .First(f => f.FieldName == "Label");
        labelField.Confidence.Should().Be(0.0);
    }

    [Fact]
    public async Task GenerateAsync_NoEvidenceLinks_FallbackDoesNotInflateVerification()
    {
        IReadOnlyList<ProvenanceEvidenceLink>? capturedLinks = null;
        _fieldVerifier.Setup(v => v.VerifyInferredField(
                "ActionType", It.IsAny<IReadOnlyList<ProvenanceEvidenceLink>>(),
                It.IsAny<IReadOnlyList<SourceBlock>>(), It.IsAny<double>()))
            .Callback<string, IReadOnlyList<ProvenanceEvidenceLink>, IReadOnlyList<SourceBlock>, double>(
                (_, links, __, ___) => capturedLinks = links)
            .Returns(new FieldVerificationResult("ActionType", VerificationStatus.Failed, 0.85, 0.0, null,
                "No evidence links"));

        var envelope = CreateValidEnvelope();
        var boardId = Guid.NewGuid();

        var result = await _sut.GenerateAsync(envelope, boardId);

        result.IsSuccess.Should().BeTrue();
        capturedLinks.Should().NotBeNull();
        capturedLinks.Should().BeEmpty("fallback link must not be passed to verification");
        var actionField = result.Value.Proposals[0].Provenance.Fields
            .First(f => f.FieldName == "ActionType");
        actionField.Confidence.Should().Be(0.0);
        actionField.EvidenceLinks.Should().HaveCount(1, "fallback link should still exist for provenance");
    }

    private IntentEnvelopeV1 CreateValidEnvelope()
    {
        var userId = Guid.NewGuid();
        var envelope = new IntentEnvelopeV1("capture", "Create a card for API review", userId);
        envelope.AddSourceBlock(0, "Create a card for API review", "capture");
        envelope.AddIntentCandidate("Create card for API review", 0.85, 0, "create-card");
        return envelope;
    }

    private IntentEnvelopeV1 CreateEnvelopeWithMultipleIntents()
    {
        var userId = Guid.NewGuid();
        var envelope = new IntentEnvelopeV1("capture", "Create card for API and move old card to done", userId);
        envelope.AddSourceBlock(0, "Create card for API and move old card to done", "capture");
        envelope.AddIntentCandidate("Create card for API", 0.9, 0, "create-card");
        envelope.AddIntentCandidate("Move card to done", 0.8, 1, "move-card");
        return envelope;
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("drop-table;--")]
    [InlineData("action with spaces")]
    public async Task GenerateAsync_MalformedActionType_ReturnsValidationFailure(string badActionType)
    {
        var envelope = CreateEnvelopeWithActionType(badActionType);
        var boardId = Guid.NewGuid();

        var result = await _sut.GenerateAsync(envelope, boardId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("actionType");
    }

    [Fact]
    public async Task GenerateAsync_MalformedActionType_DoesNotPersist()
    {
        var envelope = CreateEnvelopeWithActionType("<script>alert(1)</script>");
        var boardId = Guid.NewGuid();

        await _sut.GenerateAsync(envelope, boardId);

        _proposalRepo.Verify(
            r => r.AddAsync(It.IsAny<AutomationProposal>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(
            u => u.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GenerateAsync_MalformedActionType_LogsWarning()
    {
        var envelope = CreateEnvelopeWithActionType("<script>");
        var boardId = Guid.NewGuid();

        await _sut.GenerateAsync(envelope, boardId);

        _logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Operation input validation failed")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_ValidActionType_PassesValidation()
    {
        // Ensure legitimate identifier-like action types still work
        var envelope = CreateEnvelopeWithActionType("create-card");
        var boardId = Guid.NewGuid();

        var result = await _sut.GenerateAsync(envelope, boardId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Proposals.Should().HaveCount(1);
    }

    [Fact]
    public async Task GenerateAsync_NamespacedActionType_PassesValidation()
    {
        var envelope = CreateEnvelopeWithActionType("card.create");
        var boardId = Guid.NewGuid();

        var result = await _sut.GenerateAsync(envelope, boardId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Proposals.Should().HaveCount(1);
    }

    private IntentEnvelopeV1 CreateEnvelopeWithActionType(string actionType)
    {
        var userId = Guid.NewGuid();
        var envelope = new IntentEnvelopeV1("capture", "Delete the old review card", userId);
        envelope.AddSourceBlock(0, "Delete the old review card", "capture");
        envelope.AddIntentCandidate("Delete old review card", 0.85, 0, actionType);
        return envelope;
    }

    private static void SetStatus(IntentEnvelopeV1 envelope, EnvelopeStatus status)
    {
        var prop = typeof(IntentEnvelopeV1).GetProperty("Status");
        if (prop != null)
        {
            var backingField = typeof(IntentEnvelopeV1).GetField("<Status>k__BackingField",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            backingField?.SetValue(envelope, status);
        }
    }
}
