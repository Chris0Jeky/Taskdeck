namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Verifies extractive quotes against source text using fuzzy matching.
/// Returns a similarity score in [0.0, 1.0] where 1.0 is an exact match.
/// </summary>
public interface IFuzzyTextMatcher
{
    /// <summary>
    /// Computes the similarity between a candidate quote and the source text.
    /// The matcher finds the best substring match in the source.
    /// </summary>
    /// <param name="candidate">The extractive quote to verify.</param>
    /// <param name="source">The full source text to search within.</param>
    /// <returns>Similarity score in [0.0, 1.0].</returns>
    double ComputeSimilarity(string candidate, string source);

    /// <summary>
    /// Checks if the candidate quote matches the source text above the given threshold.
    /// </summary>
    /// <param name="candidate">The extractive quote to verify.</param>
    /// <param name="source">The full source text to search within.</param>
    /// <param name="threshold">Minimum similarity to consider a match (default 0.8).</param>
    /// <returns>True if the best match score meets or exceeds the threshold.</returns>
    bool IsMatch(string candidate, string source, double threshold = 0.8);
}
