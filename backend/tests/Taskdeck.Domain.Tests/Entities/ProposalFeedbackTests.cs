using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ProposalFeedbackTests
{
    [Fact]
    public void Constructor_ShouldCreateFeedback_WithValidArguments()
    {
        var proposalId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var feedback = new ProposalFeedback(proposalId, userId, ProposalFeedbackReason.Irrelevant);

        feedback.ProposalId.Should().Be(proposalId);
        feedback.ReportedByUserId.Should().Be(userId);
        feedback.Reason.Should().Be(ProposalFeedbackReason.Irrelevant);
        feedback.ReportedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Constructor_ShouldAllowUnspecifiedReason()
    {
        var feedback = new ProposalFeedback(Guid.NewGuid(), Guid.NewGuid(), ProposalFeedbackReason.Unspecified);
        feedback.Reason.Should().Be(ProposalFeedbackReason.Unspecified);
    }

    [Fact]
    public void Constructor_ShouldThrowValidationError_WhenProposalIdEmpty()
    {
        var act = () => new ProposalFeedback(Guid.Empty, Guid.NewGuid(), ProposalFeedbackReason.Unspecified);
        act.Should().Throw<DomainException>().Which.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrowValidationError_WhenReportedByUserIdEmpty()
    {
        var act = () => new ProposalFeedback(Guid.NewGuid(), Guid.Empty, ProposalFeedbackReason.Unspecified);
        act.Should().Throw<DomainException>().Which.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrowValidationError_WhenReasonUndefined()
    {
        var act = () => new ProposalFeedback(Guid.NewGuid(), Guid.NewGuid(), (ProposalFeedbackReason)999);
        act.Should().Throw<DomainException>().Which.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void UpdateReason_ShouldRefineReason()
    {
        var feedback = new ProposalFeedback(Guid.NewGuid(), Guid.NewGuid(), ProposalFeedbackReason.Unspecified);

        feedback.UpdateReason(ProposalFeedbackReason.TooRisky);

        feedback.Reason.Should().Be(ProposalFeedbackReason.TooRisky);
    }

    [Fact]
    public void UpdateReason_ShouldThrowValidationError_WhenReasonUndefined()
    {
        var feedback = new ProposalFeedback(Guid.NewGuid(), Guid.NewGuid(), ProposalFeedbackReason.Unspecified);
        var act = () => feedback.UpdateReason((ProposalFeedbackReason)999);
        act.Should().Throw<DomainException>().Which.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public void ProposalFeedback_ShouldBeContentFree_WithNoStringProperty()
    {
        // The no-PII invariant is structural: the entity must expose no string-typed property
        // that could carry free text. Only Guid / enum / timestamp dimensions are allowed.
        var stringProps = typeof(ProposalFeedback)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ToList();

        stringProps.Should().BeEmpty("ProposalFeedback must never carry free text / PII");
    }
}
