using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ProposalRevisionTests
{
    private readonly Guid _proposalId = Guid.NewGuid();
    private readonly Guid _editorUserId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateRevision_WithValidData()
    {
        // Arrange & Act
        var before = DateTimeOffset.UtcNow;
        var revision = new ProposalRevision(
            _proposalId,
            1,
            _editorUserId,
            "{\"operations\": []}",
            "Updated card title");

        // Assert
        revision.Id.Should().NotBe(Guid.Empty);
        revision.ProposalId.Should().Be(_proposalId);
        revision.RevisionNumber.Should().Be(1);
        revision.EditorUserId.Should().Be(_editorUserId);
        revision.RevisedPayload.Should().Be("{\"operations\": []}");
        revision.Reason.Should().Be("Updated card title");
        revision.RevisedAt.Should().BeOnOrAfter(before);
        revision.RevisedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenProposalIdIsEmpty()
    {
        var act = () => new ProposalRevision(
            Guid.Empty, 1, _editorUserId, "{}", "reason");

        act.Should().Throw<DomainException>()
            .WithMessage("ProposalId cannot be empty");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_ShouldThrow_WhenRevisionNumberIsLessThanOne(int revisionNumber)
    {
        var act = () => new ProposalRevision(
            _proposalId, revisionNumber, _editorUserId, "{}", "reason");

        act.Should().Throw<DomainException>()
            .WithMessage("RevisionNumber must be at least 1");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenEditorUserIdIsEmpty()
    {
        var act = () => new ProposalRevision(
            _proposalId, 1, Guid.Empty, "{}", "reason");

        act.Should().Throw<DomainException>()
            .WithMessage("EditorUserId cannot be empty");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenRevisedPayloadIsBlank(string payload)
    {
        var act = () => new ProposalRevision(
            _proposalId, 1, _editorUserId, payload, "reason");

        act.Should().Throw<DomainException>()
            .WithMessage("RevisedPayload cannot be empty");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenReasonIsBlank(string reason)
    {
        var act = () => new ProposalRevision(
            _proposalId, 1, _editorUserId, "{}", reason);

        act.Should().Throw<DomainException>()
            .WithMessage("Reason cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenReasonExceedsMaxLength()
    {
        var longReason = new string('a', 501);

        var act = () => new ProposalRevision(
            _proposalId, 1, _editorUserId, "{}", longReason);

        act.Should().Throw<DomainException>()
            .WithMessage("Reason cannot exceed 500 characters");
    }

    [Fact]
    public void Constructor_ShouldAcceptReason_AtExactMaxLength()
    {
        var reason = new string('a', 500);

        var revision = new ProposalRevision(
            _proposalId, 1, _editorUserId, "{}", reason);

        revision.Reason.Should().HaveLength(500);
    }

    [Fact]
    public void Immutability_PropertiesAreReadOnly()
    {
        // Verify that properties have private setters (enforced by C# compiler)
        // The entity uses private set throughout, confirming immutability after construction.
        var revision = new ProposalRevision(
            _proposalId, 1, _editorUserId, "{\"data\": true}", "initial edit");

        // All properties should retain their construction values
        revision.ProposalId.Should().Be(_proposalId);
        revision.RevisionNumber.Should().Be(1);
        revision.EditorUserId.Should().Be(_editorUserId);
        revision.RevisedPayload.Should().Be("{\"data\": true}");
        revision.Reason.Should().Be("initial edit");
    }

    [Fact]
    public void Constructor_ShouldAcceptHighRevisionNumbers()
    {
        var revision = new ProposalRevision(
            _proposalId, 100, _editorUserId, "{}", "hundredth edit");

        revision.RevisionNumber.Should().Be(100);
    }

    [Fact]
    public void Constructor_ShouldAcceptLargeJsonPayload()
    {
        var largePayload = "{\"operations\": [" + string.Join(",",
            Enumerable.Range(0, 100).Select(i => $"{{\"seq\": {i}}}")) + "]}";

        var revision = new ProposalRevision(
            _proposalId, 1, _editorUserId, largePayload, "bulk edit");

        revision.RevisedPayload.Should().Be(largePayload);
    }
}
