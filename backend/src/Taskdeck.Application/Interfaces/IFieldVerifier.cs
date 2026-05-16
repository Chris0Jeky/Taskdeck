using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Verifies provenance fields against their source material.
/// Extractive fields are verified via fuzzy text matching.
/// Inferred fields are verified by resolving their evidence links.
/// </summary>
public interface IFieldVerifier
{
    /// <summary>
    /// Verifies an extractive field's quote against the source text.
    /// </summary>
    /// <param name="fieldName">The name of the field being verified.</param>
    /// <param name="extractiveQuote">The claimed quote from the source.</param>
    /// <param name="sourceText">The full source text to verify against.</param>
    /// <param name="originalConfidence">The confidence assigned before verification.</param>
    /// <returns>A verification result with similarity score and adjusted confidence.</returns>
    FieldVerificationResult VerifyExtractiveField(
        string fieldName,
        string extractiveQuote,
        string sourceText,
        double originalConfidence);

    /// <summary>
    /// Verifies an inferred field by checking whether its evidence links resolve.
    /// </summary>
    /// <param name="fieldName">The name of the field being verified.</param>
    /// <param name="evidenceLinks">Evidence links supporting this inference.</param>
    /// <param name="availableSourceBlocks">Source blocks available for resolution.</param>
    /// <param name="originalConfidence">The confidence assigned before verification.</param>
    /// <returns>A verification result indicating whether evidence resolved.</returns>
    FieldVerificationResult VerifyInferredField(
        string fieldName,
        IReadOnlyList<ProvenanceEvidenceLink> evidenceLinks,
        IReadOnlyList<SourceBlock> availableSourceBlocks,
        double originalConfidence);
}
