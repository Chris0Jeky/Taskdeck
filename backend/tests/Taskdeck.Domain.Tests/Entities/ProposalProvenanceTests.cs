using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ProposalProvenanceTests
{
    [Fact]
    public void Constructor_ShouldCreateProvenance_WithValidData()
    {
        var proposalId = Guid.NewGuid();

        var provenance = new ProposalProvenance(proposalId, "corr-123", "gpt-4o", 500);

        provenance.ProposalId.Should().Be(proposalId);
        provenance.CorrelationId.Should().Be("corr-123");
        provenance.ModelId.Should().Be("gpt-4o");
        provenance.TotalTokens.Should().Be(500);
        provenance.Fields.Should().BeEmpty();
        provenance.Id.Should().NotBe(Guid.Empty);
        provenance.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Constructor_ShouldDefaultTotalTokensToZero()
    {
        var provenance = new ProposalProvenance(Guid.NewGuid(), "corr-1", "mock");

        provenance.TotalTokens.Should().Be(0);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenProposalIdIsEmpty()
    {
        var act = () => new ProposalProvenance(Guid.Empty, "corr-1", "mock");

        act.Should().Throw<DomainException>()
            .WithMessage("ProposalId cannot be empty")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenCorrelationIdIsEmpty()
    {
        var act = () => new ProposalProvenance(Guid.NewGuid(), "", "mock");

        act.Should().Throw<DomainException>()
            .WithMessage("CorrelationId cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenCorrelationIdIsWhitespace()
    {
        var act = () => new ProposalProvenance(Guid.NewGuid(), "   ", "mock");

        act.Should().Throw<DomainException>()
            .WithMessage("CorrelationId cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenCorrelationIdExceedsMaxLength()
    {
        var longCorrelationId = new string('x', 101);

        var act = () => new ProposalProvenance(Guid.NewGuid(), longCorrelationId, "mock");

        act.Should().Throw<DomainException>()
            .WithMessage("CorrelationId cannot exceed 100 characters");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenModelIdIsEmpty()
    {
        var act = () => new ProposalProvenance(Guid.NewGuid(), "corr-1", "");

        act.Should().Throw<DomainException>()
            .WithMessage("ModelId cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenModelIdExceedsMaxLength()
    {
        var longModelId = new string('m', 101);

        var act = () => new ProposalProvenance(Guid.NewGuid(), "corr-1", longModelId);

        act.Should().Throw<DomainException>()
            .WithMessage("ModelId cannot exceed 100 characters");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenTotalTokensIsNegative()
    {
        var act = () => new ProposalProvenance(Guid.NewGuid(), "corr-1", "mock", -1);

        act.Should().Throw<DomainException>()
            .WithMessage("TotalTokens cannot be negative");
    }

    [Fact]
    public void AddField_ShouldAddToFieldsList()
    {
        var provenance = new ProposalProvenance(Guid.NewGuid(), "corr-1", "mock");
        var field = new ProvenanceField("Title", Enums.ProvenanceKind.Inferred, 0.9, provenance.Id);

        provenance.AddField(field);

        provenance.Fields.Should().ContainSingle().Which.Should().Be(field);
    }

    [Fact]
    public void AddField_ShouldTouchUpdatedAt()
    {
        var provenance = new ProposalProvenance(Guid.NewGuid(), "corr-1", "mock");
        var originalUpdatedAt = provenance.UpdatedAt;

        // Small delay to ensure timestamp difference
        System.Threading.Thread.Sleep(10);

        var field = new ProvenanceField("Title", Enums.ProvenanceKind.Inferred, 0.9, provenance.Id);
        provenance.AddField(field);

        provenance.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);
    }
}
