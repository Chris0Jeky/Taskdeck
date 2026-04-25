using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class IntentEnvelopeV1Tests
{
    private readonly Guid _userId = Guid.NewGuid();

    private static TaskdeckProposalBatch CreateSealedBatch(IntentEnvelopeV1 envelope, Guid userId)
    {
        var batch = envelope.CreateBatch(userId, "Batch summary");
        batch.AddProposalId(Guid.NewGuid());
        batch.Seal();
        return batch;
    }

    [Fact]
    public void Constructor_ShouldCreateEnvelope_WithValidData()
    {
        var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var envelope = new IntentEnvelopeV1("capture", "Fix the login bug", _userId, capturedAt, "corr-123");

        envelope.Id.Should().NotBe(Guid.Empty);
        envelope.Version.Should().Be(1);
        envelope.Source.Should().Be("capture");
        envelope.RawContent.Should().Be("Fix the login bug");
        envelope.UserId.Should().Be(_userId);
        envelope.CapturedAt.Should().Be(capturedAt);
        envelope.CorrelationId.Should().Be("corr-123");
        envelope.Status.Should().Be(EnvelopeStatus.Created);
        envelope.SourceBlocks.Should().BeEmpty();
        envelope.IntentCandidates.Should().BeEmpty();
        envelope.Batches.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ShouldDefaultCapturedAtToNow()
    {
        var before = DateTimeOffset.UtcNow;
        var envelope = new IntentEnvelopeV1("chat", "Some input", _userId);
        var after = DateTimeOffset.UtcNow;

        envelope.CapturedAt.Should().BeOnOrAfter(before);
        envelope.CapturedAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void Constructor_ShouldRejectEmptySource()
    {
        var act = () => new IntentEnvelopeV1("", "content", _userId);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectSourceExceeding50Characters()
    {
        var longSource = new string('x', 51);
        var act = () => new IntentEnvelopeV1(longSource, "content", _userId);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyRawContent()
    {
        var act = () => new IntentEnvelopeV1("capture", "", _userId);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectRawContentExceeding100000Characters()
    {
        var longContent = new string('x', 100_001);
        var act = () => new IntentEnvelopeV1("capture", longContent, _userId);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyUserId()
    {
        var act = () => new IntentEnvelopeV1("capture", "content", Guid.Empty);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldTrimCorrelationId()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId, correlationId: "  corr-456  ");

        envelope.CorrelationId.Should().Be("corr-456");
    }

    [Fact]
    public void Constructor_ShouldSetNullCorrelationId_WhenWhitespace()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId, correlationId: "   ");

        envelope.CorrelationId.Should().BeNull();
    }

    // ── AddSourceBlock ────────────────────────────────────────────────

    [Fact]
    public void AddSourceBlock_ShouldAddBlock_WhenCreated()
    {
        var envelope = new IntentEnvelopeV1("capture", "Fix the login bug", _userId);

        var block = envelope.AddSourceBlock(0, "Fix the login bug", "capture");

        block.Should().NotBeNull();
        block.EnvelopeId.Should().Be(envelope.Id);
        block.Position.Should().Be(0);
        envelope.SourceBlocks.Should().HaveCount(1);
    }

    [Fact]
    public void AddSourceBlock_ShouldRejectAfterExtracting()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);
        envelope.AddIntentCandidate("Some intent", 0.8, 0); // transitions to Extracting

        var act = () => envelope.AddSourceBlock(0, "content", "capture");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("InvalidOperation");
    }

    // ── AddIntentCandidate ────────────────────────────────────────────

    [Fact]
    public void AddIntentCandidate_ShouldTransitionToExtracting()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);

        envelope.AddIntentCandidate("Create card", 0.9, 0, "create-card");

        envelope.Status.Should().Be(EnvelopeStatus.Extracting);
        envelope.IntentCandidates.Should().HaveCount(1);
    }

    [Fact]
    public void AddIntentCandidate_ShouldAllowMultipleCandidates()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);

        envelope.AddIntentCandidate("Create card", 0.9, 0, "create-card");
        envelope.AddIntentCandidate("Update column", 0.7, 1, "update-column");

        envelope.IntentCandidates.Should().HaveCount(2);
    }

    [Fact]
    public void AddIntentCandidate_ShouldNotTransitionStatus_WhenCandidateValidationFails()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);

        // Attempt to add a candidate with an invalid label (empty)
        var act = () => envelope.AddIntentCandidate("", 0.5, 0);

        act.Should().Throw<DomainException>();
        envelope.Status.Should().Be(EnvelopeStatus.Created,
            "status must not change when candidate construction fails");
        envelope.IntentCandidates.Should().BeEmpty(
            "no candidate should be added when validation fails");
    }

    [Fact]
    public void AddIntentCandidate_ShouldRejectAfterProcessed()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);
        envelope.AddIntentCandidate("intent", 0.5, 0);
        CreateSealedBatch(envelope, _userId);
        envelope.MarkProcessed();

        var act = () => envelope.AddIntentCandidate("another intent", 0.3, 1);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("InvalidOperation");
    }

    [Fact]
    public void AddIntentCandidate_ShouldRejectAfterFailed()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);
        envelope.MarkFailed("something broke");

        var act = () => envelope.AddIntentCandidate("intent", 0.5, 0);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("InvalidOperation");
    }

    // ── CreateBatch ───────────────────────────────────────────────────

    [Fact]
    public void CreateBatch_ShouldCreateBatch_WhenExtracting()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);
        envelope.AddIntentCandidate("intent", 0.5, 0);

        var batch = envelope.CreateBatch(_userId, "Batch summary");

        batch.Should().NotBeNull();
        batch.EnvelopeId.Should().Be(envelope.Id);
        envelope.Batches.Should().HaveCount(1);
    }

    [Fact]
    public void CreateBatch_ShouldRejectWhenCreated()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);

        var act = () => envelope.CreateBatch(_userId, "Summary");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("InvalidOperation");
    }

    // ── MarkProcessed ─────────────────────────────────────────────────

    [Fact]
    public void MarkProcessed_ShouldTransitionToProcessed()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);
        envelope.AddIntentCandidate("intent", 0.5, 0);
        CreateSealedBatch(envelope, _userId);

        envelope.MarkProcessed();

        envelope.Status.Should().Be(EnvelopeStatus.Processed);
    }

    [Fact]
    public void MarkProcessed_ShouldRejectWithoutCandidates()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);
        // Must be in Extracting status -- but with no candidates we're still Created
        // so this should fail on status check before reaching candidate check

        var act = () => envelope.MarkProcessed();

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("InvalidOperation");
    }

    [Fact]
    public void MarkProcessed_ShouldRejectWithoutProposalBatch()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);
        envelope.AddIntentCandidate("intent", 0.5, 0);

        var act = () => envelope.MarkProcessed();

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void MarkProcessed_ShouldRejectWhenAnyBatchIsDraft()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);
        envelope.AddIntentCandidate("intent", 0.5, 0);
        var sealedBatch = CreateSealedBatch(envelope, _userId);
        var draftBatch = envelope.CreateBatch(_userId, "Draft batch");

        var act = () => envelope.MarkProcessed();

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("InvalidOperation");
        envelope.Status.Should().Be(EnvelopeStatus.Extracting);
        sealedBatch.Status.Should().Be(ProposalBatchStatus.Sealed);
        draftBatch.Status.Should().Be(ProposalBatchStatus.Draft);
    }

    [Fact]
    public void MarkProcessed_ShouldRejectWhenAlreadyProcessed()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);
        envelope.AddIntentCandidate("intent", 0.5, 0);
        CreateSealedBatch(envelope, _userId);
        envelope.MarkProcessed();

        var act = () => envelope.MarkProcessed();

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("InvalidOperation");
    }

    // ── MarkFailed ────────────────────────────────────────────────────

    [Fact]
    public void MarkFailed_ShouldTransitionToFailed()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);

        envelope.MarkFailed("Processing error");

        envelope.Status.Should().Be(EnvelopeStatus.Failed);
        envelope.FailureReason.Should().Be("Processing error");
    }

    [Fact]
    public void MarkFailed_ShouldStoreNullReason_WhenWhitespace()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);

        envelope.MarkFailed("   ");

        envelope.Status.Should().Be(EnvelopeStatus.Failed);
        envelope.FailureReason.Should().BeNull();
    }

    [Fact]
    public void MarkFailed_ShouldTrimReason()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);

        envelope.MarkFailed("  trimmed reason  ");

        envelope.FailureReason.Should().Be("trimmed reason");
    }

    [Fact]
    public void FailureReason_ShouldBeNull_WhenNotFailed()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);

        envelope.FailureReason.Should().BeNull();
    }

    [Fact]
    public void MarkFailed_ShouldRejectWhenAlreadyProcessed()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);
        envelope.AddIntentCandidate("intent", 0.5, 0);
        CreateSealedBatch(envelope, _userId);
        envelope.MarkProcessed();

        var act = () => envelope.MarkFailed("too late");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("InvalidOperation");
    }

    [Fact]
    public void MarkFailed_ShouldAllowFromExtractingStatus()
    {
        var envelope = new IntentEnvelopeV1("capture", "content", _userId);
        envelope.AddIntentCandidate("intent", 0.5, 0);

        envelope.MarkFailed("extraction failed");

        envelope.Status.Should().Be(EnvelopeStatus.Failed);
    }

    // ── Full lifecycle ────────────────────────────────────────────────

    [Fact]
    public void FullLifecycle_ShouldWorkEndToEnd()
    {
        // Create envelope
        var envelope = new IntentEnvelopeV1("capture", "Fix login bug and add tests", _userId, correlationId: "cap-1");
        envelope.Status.Should().Be(EnvelopeStatus.Created);

        // Add source blocks
        var block = envelope.AddSourceBlock(0, "Fix login bug and add tests", "capture", "cap-1");
        var span = block.AddSpan(0, 13, "Fix login bug");

        // Add intent candidates (transitions to Extracting)
        var intent1 = envelope.AddIntentCandidate("Fix login bug", 0.95, 0, "create-card");
        var intent2 = envelope.AddIntentCandidate("Add tests", 0.8, 1, "create-card");
        envelope.Status.Should().Be(EnvelopeStatus.Extracting);

        // Link evidence
        var evidenceLink = new EvidenceLink(intent1.Id, span.Id, 0.95, "Direct mention");
        intent1.AddEvidenceLink(evidenceLink);

        // Create batch
        var batch = envelope.CreateBatch(_userId, "Two cards from capture");
        batch.AddProposalId(Guid.NewGuid());
        batch.AddProposalId(Guid.NewGuid());
        batch.Seal();

        // Mark processed
        envelope.MarkProcessed();
        envelope.Status.Should().Be(EnvelopeStatus.Processed);

        // Verify structure
        envelope.SourceBlocks.Should().HaveCount(1);
        envelope.IntentCandidates.Should().HaveCount(2);
        envelope.Batches.Should().HaveCount(1);
        block.Spans.Should().HaveCount(1);
        intent1.EvidenceLinks.Should().HaveCount(1);
        batch.ProposalIds.Should().HaveCount(2);
        batch.Status.Should().Be(ProposalBatchStatus.Sealed);
    }
}
