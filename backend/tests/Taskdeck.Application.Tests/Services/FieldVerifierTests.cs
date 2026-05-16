using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class FieldVerifierTests
{
    private readonly Mock<IFuzzyTextMatcher> _matcher = new();
    private readonly FieldVerifier _sut;

    public FieldVerifierTests()
    {
        _sut = new FieldVerifier(_matcher.Object);
    }

    [Fact]
    public void VerifyExtractiveField_ExactMatch_ReturnsVerified()
    {
        _matcher.Setup(m => m.ComputeSimilarity("hello world", "this is hello world here"))
            .Returns(0.95);

        var result = _sut.VerifyExtractiveField("Title", "hello world", "this is hello world here", 0.9);

        result.Status.Should().Be(VerificationStatus.Verified);
        result.AdjustedConfidence.Should().Be(0.9);
        result.SimilarityScore.Should().Be(0.95);
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void VerifyExtractiveField_PartialMatch_ReturnsDowngraded()
    {
        _matcher.Setup(m => m.ComputeSimilarity("hello wrld", "this is hello world here"))
            .Returns(0.7);

        var result = _sut.VerifyExtractiveField("Title", "hello wrld", "this is hello world here", 0.9);

        result.Status.Should().Be(VerificationStatus.Downgraded);
        result.AdjustedConfidence.Should().BeApproximately(0.9 * 0.7, 0.001);
        result.SimilarityScore.Should().Be(0.7);
        result.Reason.Should().Contain("Partial match");
    }

    [Fact]
    public void VerifyExtractiveField_NoMatch_ReturnsFailed()
    {
        _matcher.Setup(m => m.ComputeSimilarity("completely different", "source text"))
            .Returns(0.2);

        var result = _sut.VerifyExtractiveField("Title", "completely different", "source text", 0.8);

        result.Status.Should().Be(VerificationStatus.Failed);
        result.AdjustedConfidence.Should().Be(0.0);
        result.SimilarityScore.Should().Be(0.2);
        result.Reason.Should().Contain("not found in source");
    }

    [Fact]
    public void VerifyExtractiveField_EmptySource_ReturnsFailed()
    {
        var result = _sut.VerifyExtractiveField("Title", "hello", "", 0.9);

        result.Status.Should().Be(VerificationStatus.Failed);
        result.AdjustedConfidence.Should().Be(0.0);
        result.SimilarityScore.Should().BeNull();
        result.Reason.Should().Contain("empty or unavailable");
    }

    [Fact]
    public void VerifyExtractiveField_NullSource_ReturnsFailed()
    {
        var result = _sut.VerifyExtractiveField("Title", "hello", null!, 0.9);

        result.Status.Should().Be(VerificationStatus.Failed);
        result.AdjustedConfidence.Should().Be(0.0);
    }

    [Fact]
    public void VerifyExtractiveField_AtVerifiedThreshold_ReturnsVerified()
    {
        _matcher.Setup(m => m.ComputeSimilarity("quote", "source")).Returns(0.85);

        var result = _sut.VerifyExtractiveField("Field", "quote", "source", 0.9);

        result.Status.Should().Be(VerificationStatus.Verified);
    }

    [Fact]
    public void VerifyExtractiveField_JustBelowVerifiedThreshold_ReturnsDowngraded()
    {
        _matcher.Setup(m => m.ComputeSimilarity("quote", "source")).Returns(0.84);

        var result = _sut.VerifyExtractiveField("Field", "quote", "source", 0.9);

        result.Status.Should().Be(VerificationStatus.Downgraded);
    }

    [Fact]
    public void VerifyExtractiveField_AtDowngradeThreshold_ReturnsDowngraded()
    {
        _matcher.Setup(m => m.ComputeSimilarity("quote", "source")).Returns(0.5);

        var result = _sut.VerifyExtractiveField("Field", "quote", "source", 0.9);

        result.Status.Should().Be(VerificationStatus.Downgraded);
    }

    [Fact]
    public void VerifyExtractiveField_BelowDowngradeThreshold_ReturnsFailed()
    {
        _matcher.Setup(m => m.ComputeSimilarity("quote", "source")).Returns(0.49);

        var result = _sut.VerifyExtractiveField("Field", "quote", "source", 0.9);

        result.Status.Should().Be(VerificationStatus.Failed);
    }

    [Fact]
    public void VerifyExtractiveField_EmptyFieldName_Throws()
    {
        var act = () => _sut.VerifyExtractiveField("", "quote", "source", 0.9);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void VerifyExtractiveField_EmptyQuote_Throws()
    {
        var act = () => _sut.VerifyExtractiveField("Title", "", "source", 0.9);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void VerifyInferredField_AllLinksResolve_ReturnsVerified()
    {
        var blockId = Guid.NewGuid();
        var fieldId = Guid.NewGuid();
        var links = new List<ProvenanceEvidenceLink>
        {
            new("capture", blockId.ToString(), fieldId, spanStart: 0, spanEnd: 5)
        };
        var blocks = new List<SourceBlock>
        {
            CreateSourceBlock(blockId, "hello world")
        };

        var result = _sut.VerifyInferredField("ActionType", links, blocks, 0.8);

        result.Status.Should().Be(VerificationStatus.Verified);
        result.AdjustedConfidence.Should().Be(0.8);
        result.SimilarityScore.Should().Be(1.0);
    }

    [Fact]
    public void VerifyInferredField_SomeLinksResolve_ReturnsDowngraded()
    {
        var blockId = Guid.NewGuid();
        var fieldId = Guid.NewGuid();
        var links = new List<ProvenanceEvidenceLink>
        {
            new("capture", blockId.ToString(), fieldId),
            new("capture", Guid.NewGuid().ToString(), fieldId)
        };
        var blocks = new List<SourceBlock>
        {
            CreateSourceBlock(blockId, "hello world")
        };

        var result = _sut.VerifyInferredField("ActionType", links, blocks, 0.8);

        result.Status.Should().Be(VerificationStatus.Downgraded);
        result.AdjustedConfidence.Should().BeApproximately(0.4, 0.001);
        result.Reason.Should().Contain("1/2");
    }

    [Fact]
    public void VerifyInferredField_NoLinksResolve_ReturnsFailed()
    {
        var fieldId = Guid.NewGuid();
        var links = new List<ProvenanceEvidenceLink>
        {
            new("capture", Guid.NewGuid().ToString(), fieldId)
        };
        var blocks = new List<SourceBlock>
        {
            CreateSourceBlock(Guid.NewGuid(), "hello world")
        };

        var result = _sut.VerifyInferredField("ActionType", links, blocks, 0.8);

        result.Status.Should().Be(VerificationStatus.Failed);
        result.AdjustedConfidence.Should().Be(0.0);
    }

    [Fact]
    public void VerifyInferredField_NoLinks_ReturnsFailed()
    {
        var blocks = new List<SourceBlock>
        {
            CreateSourceBlock(Guid.NewGuid(), "hello world")
        };

        var result = _sut.VerifyInferredField("ActionType", new List<ProvenanceEvidenceLink>(), blocks, 0.8);

        result.Status.Should().Be(VerificationStatus.Failed);
        result.Reason.Should().Contain("No evidence links");
    }

    [Fact]
    public void VerifyInferredField_LinkWithInvalidSpan_DoesNotResolve()
    {
        var blockId = Guid.NewGuid();
        var fieldId = Guid.NewGuid();
        var links = new List<ProvenanceEvidenceLink>
        {
            new("capture", blockId.ToString(), fieldId, spanStart: 0, spanEnd: 100)
        };
        var blocks = new List<SourceBlock>
        {
            CreateSourceBlock(blockId, "short")
        };

        var result = _sut.VerifyInferredField("ActionType", links, blocks, 0.8);

        result.Status.Should().Be(VerificationStatus.Failed);
        result.AdjustedConfidence.Should().Be(0.0);
    }

    [Fact]
    public void VerifyInferredField_LinkWithoutSpan_ResolvesIfBlockFound()
    {
        var blockId = Guid.NewGuid();
        var fieldId = Guid.NewGuid();
        var links = new List<ProvenanceEvidenceLink>
        {
            new("capture", blockId.ToString(), fieldId)
        };
        var blocks = new List<SourceBlock>
        {
            CreateSourceBlock(blockId, "any content")
        };

        var result = _sut.VerifyInferredField("ActionType", links, blocks, 0.9);

        result.Status.Should().Be(VerificationStatus.Verified);
        result.AdjustedConfidence.Should().Be(0.9);
    }

    [Fact]
    public void VerifyInferredField_ResolvesViaSourceReferenceId()
    {
        var fieldId = Guid.NewGuid();
        var links = new List<ProvenanceEvidenceLink>
        {
            new("capture", "ref-123", fieldId)
        };
        var blocks = new List<SourceBlock>
        {
            CreateSourceBlockWithRef(Guid.NewGuid(), "content", "ref-123")
        };

        var result = _sut.VerifyInferredField("ActionType", links, blocks, 0.7);

        result.Status.Should().Be(VerificationStatus.Verified);
    }

    [Fact]
    public void VerifyInferredField_EmptyFieldName_Throws()
    {
        var act = () => _sut.VerifyInferredField(
            "", new List<ProvenanceEvidenceLink>(), new List<SourceBlock>(), 0.8);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void VerifyInferredField_NullLinks_Throws()
    {
        var act = () => _sut.VerifyInferredField("Field", null!, new List<SourceBlock>(), 0.8);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void VerifyInferredField_NullBlocks_Throws()
    {
        var act = () => _sut.VerifyInferredField(
            "Field", new List<ProvenanceEvidenceLink>(), null!, 0.8);
        act.Should().Throw<ArgumentNullException>();
    }

    private static SourceBlock CreateSourceBlock(Guid id, string content)
    {
        var block = new SourceBlock(Guid.NewGuid(), 0, content, "capture");
        SetEntityId(block, id);
        return block;
    }

    private static SourceBlock CreateSourceBlockWithRef(Guid id, string content, string sourceRef)
    {
        var block = new SourceBlock(Guid.NewGuid(), 0, content, "capture", sourceRef);
        SetEntityId(block, id);
        return block;
    }

    private static void SetEntityId(object entity, Guid id)
    {
        var field = entity.GetType().BaseType?.GetField("<Id>k__BackingField",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(entity, id);
    }
}
