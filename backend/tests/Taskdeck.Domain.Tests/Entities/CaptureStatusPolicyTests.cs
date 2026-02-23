using FluentAssertions;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class CaptureStatusPolicyTests
{
    [Fact]
    public void MapFromQueueStatus_ShouldMapPendingToNew()
    {
        var status = CaptureStatusPolicy.MapFromQueueStatus(RequestStatus.Pending);

        status.Should().Be(CaptureStatus.New);
    }

    [Fact]
    public void MapFromQueueStatus_ShouldMapCompletedToProposalCreated_WhenProposalExists()
    {
        var status = CaptureStatusPolicy.MapFromQueueStatus(
            RequestStatus.Completed,
            hasLinkedProposal: true);

        status.Should().Be(CaptureStatus.ProposalCreated);
    }

    [Fact]
    public void MapFromQueueStatus_ShouldMapToConverted_WhenConvertedFlagIsSet()
    {
        var status = CaptureStatusPolicy.MapFromQueueStatus(
            RequestStatus.Completed,
            hasLinkedProposal: true,
            isConverted: true);

        status.Should().Be(CaptureStatus.Converted);
    }

    [Theory]
    [InlineData(CaptureStatus.New, CaptureStatus.Triaging, true)]
    [InlineData(CaptureStatus.New, CaptureStatus.Converted, false)]
    [InlineData(CaptureStatus.Triaging, CaptureStatus.ProposalCreated, true)]
    [InlineData(CaptureStatus.ProposalCreated, CaptureStatus.Converted, true)]
    [InlineData(CaptureStatus.Converted, CaptureStatus.Triaging, false)]
    [InlineData(CaptureStatus.Ignored, CaptureStatus.Triaging, false)]
    [InlineData(CaptureStatus.Failed, CaptureStatus.Triaging, true)]
    public void CanTransition_ShouldReturnExpectedResult(
        CaptureStatus from,
        CaptureStatus to,
        bool expected)
    {
        var canTransition = CaptureStatusPolicy.CanTransition(from, to);

        canTransition.Should().Be(expected);
    }
}
