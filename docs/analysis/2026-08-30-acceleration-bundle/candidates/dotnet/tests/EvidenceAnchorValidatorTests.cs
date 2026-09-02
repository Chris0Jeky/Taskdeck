using Taskdeck.Acceleration.Candidates.ContextFabric;
using Xunit;

namespace Taskdeck.Acceleration.Candidates.Tests.ContextFabric;

public sealed class EvidenceAnchorValidatorTests
{
    [Fact]
    public void Accepts_half_open_text_span()
    {
        var result = EvidenceAnchorValidator.Validate(
            new CandidateEvidenceAnchor(CandidateEvidenceAnchorKind.TextSpan, StartOffset: 0, EndOffset: 5));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Rejects_mixed_kind_fields()
    {
        var result = EvidenceAnchorValidator.Validate(
            new CandidateEvidenceAnchor(
                CandidateEvidenceAnchorKind.TimeRange,
                StartMilliseconds: 0,
                EndMilliseconds: 10,
                PageNumber: 1));
        Assert.False(result.IsValid);
        Assert.Equal("evidence_anchor_fields_mismatch", result.ErrorCode);
    }

    [Fact]
    public void Rejects_out_of_bounds_rectangle()
    {
        var result = EvidenceAnchorValidator.Validate(
            new CandidateEvidenceAnchor(
                CandidateEvidenceAnchorKind.ImageRegion,
                Rectangle: new NormalizedRectangle(0.8, 0.1, 0.3, 0.2)));
        Assert.False(result.IsValid);
    }
}
