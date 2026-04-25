using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class TaskdeckProposalBatchTests
{
    private readonly Guid _envelopeId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateBatch_WithValidData()
    {
        var batch = new TaskdeckProposalBatch(_envelopeId, _userId, "Create two cards from meeting notes");

        batch.Id.Should().NotBe(Guid.Empty);
        batch.EnvelopeId.Should().Be(_envelopeId);
        batch.RequestedByUserId.Should().Be(_userId);
        batch.Summary.Should().Be("Create two cards from meeting notes");
        batch.SchemaVersion.Should().Be(1);
        batch.Status.Should().Be(ProposalBatchStatus.Draft);
        batch.ProposalIds.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyEnvelopeId()
    {
        var act = () => new TaskdeckProposalBatch(Guid.Empty, _userId, "Summary");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectEmptyUserId()
    {
        var act = () => new TaskdeckProposalBatch(_envelopeId, Guid.Empty, "Summary");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectEmptySummary()
    {
        var act = () => new TaskdeckProposalBatch(_envelopeId, _userId, "");

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectSummaryExceeding1000Characters()
    {
        var longSummary = new string('x', 1001);
        var act = () => new TaskdeckProposalBatch(_envelopeId, _userId, longSummary);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Constructor_ShouldRejectSchemaVersionLessThan1()
    {
        var act = () => new TaskdeckProposalBatch(_envelopeId, _userId, "Summary", schemaVersion: 0);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void AddProposalId_ShouldAddId()
    {
        var batch = new TaskdeckProposalBatch(_envelopeId, _userId, "Summary");
        var proposalId = Guid.NewGuid();

        batch.AddProposalId(proposalId);

        batch.ProposalIds.Should().ContainSingle().Which.Should().Be(proposalId);
    }

    [Fact]
    public void AddProposalId_ShouldRejectEmptyId()
    {
        var batch = new TaskdeckProposalBatch(_envelopeId, _userId, "Summary");

        var act = () => batch.AddProposalId(Guid.Empty);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void AddProposalId_ShouldRejectDuplicateId()
    {
        var batch = new TaskdeckProposalBatch(_envelopeId, _userId, "Summary");
        var proposalId = Guid.NewGuid();
        batch.AddProposalId(proposalId);

        var act = () => batch.AddProposalId(proposalId);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("Conflict");
    }

    [Fact]
    public void AddProposalId_ShouldRejectWhenSealed()
    {
        var batch = new TaskdeckProposalBatch(_envelopeId, _userId, "Summary");
        batch.AddProposalId(Guid.NewGuid());
        batch.Seal();

        var act = () => batch.AddProposalId(Guid.NewGuid());

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("InvalidOperation");
    }

    [Fact]
    public void Seal_ShouldTransitionToSealed()
    {
        var batch = new TaskdeckProposalBatch(_envelopeId, _userId, "Summary");
        batch.AddProposalId(Guid.NewGuid());

        batch.Seal();

        batch.Status.Should().Be(ProposalBatchStatus.Sealed);
    }

    [Fact]
    public void Seal_ShouldRejectEmptyBatch()
    {
        var batch = new TaskdeckProposalBatch(_envelopeId, _userId, "Summary");

        var act = () => batch.Seal();

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("ValidationError");
    }

    [Fact]
    public void Seal_ShouldRejectWhenAlreadySealed()
    {
        var batch = new TaskdeckProposalBatch(_envelopeId, _userId, "Summary");
        batch.AddProposalId(Guid.NewGuid());
        batch.Seal();

        var act = () => batch.Seal();

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("InvalidOperation");
    }

    [Fact]
    public void Complete_ShouldTransitionToCompleted()
    {
        var batch = new TaskdeckProposalBatch(_envelopeId, _userId, "Summary");
        batch.AddProposalId(Guid.NewGuid());
        batch.Seal();

        batch.Complete();

        batch.Status.Should().Be(ProposalBatchStatus.Completed);
    }

    [Fact]
    public void Complete_ShouldRejectWhenNotSealed()
    {
        var batch = new TaskdeckProposalBatch(_envelopeId, _userId, "Summary");

        var act = () => batch.Complete();

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("InvalidOperation");
    }

    [Fact]
    public void Discard_ShouldTransitionToDiscarded()
    {
        var batch = new TaskdeckProposalBatch(_envelopeId, _userId, "Summary");

        batch.Discard();

        batch.Status.Should().Be(ProposalBatchStatus.Discarded);
    }

    [Fact]
    public void Discard_ShouldRejectWhenCompleted()
    {
        var batch = new TaskdeckProposalBatch(_envelopeId, _userId, "Summary");
        batch.AddProposalId(Guid.NewGuid());
        batch.Seal();
        batch.Complete();

        var act = () => batch.Discard();

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be("InvalidOperation");
    }

    [Fact]
    public void Discard_ShouldAllowFromSealedStatus()
    {
        var batch = new TaskdeckProposalBatch(_envelopeId, _userId, "Summary");
        batch.AddProposalId(Guid.NewGuid());
        batch.Seal();

        batch.Discard();

        batch.Status.Should().Be(ProposalBatchStatus.Discarded);
    }
}
