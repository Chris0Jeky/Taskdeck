using Taskdeck.Application.Interfaces;

namespace Taskdeck.Application.Services;

/// <summary>
/// Verifies extractive quotes against source text using Levenshtein-distance-based
/// fuzzy matching. Finds the best matching substring in the source for the candidate.
///
/// Implementation uses a sliding window approach: for each window of the candidate's
/// length in the source, compute edit distance and derive similarity.
/// </summary>
public class FuzzyTextMatcher : IFuzzyTextMatcher
{
    public double ComputeSimilarity(string candidate, string source)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(source))
            return 0.0;

        // Normalize: lowercase, collapse whitespace
        var normalizedCandidate = Normalize(candidate);
        var normalizedSource = Normalize(source);

        if (normalizedCandidate.Length == 0 || normalizedSource.Length == 0)
            return 0.0;

        // Exact substring check first (fast path)
        if (normalizedSource.Contains(normalizedCandidate, StringComparison.Ordinal))
            return 1.0;

        // If the candidate is longer than the source, compute full edit distance
        if (normalizedCandidate.Length > normalizedSource.Length)
        {
            var fullDistance = ComputeLevenshteinDistance(normalizedCandidate, normalizedSource);
            return 1.0 - (double)fullDistance / Math.Max(normalizedCandidate.Length, normalizedSource.Length);
        }

        // Sliding window: find the best substring match
        var windowSize = normalizedCandidate.Length;
        var bestDistance = int.MaxValue;

        // Allow some tolerance in window size for insertions/deletions
        var minWindow = Math.Max(1, windowSize - windowSize / 4);
        var maxWindow = Math.Min(normalizedSource.Length, windowSize + windowSize / 4);

        for (var wSize = minWindow; wSize <= maxWindow; wSize++)
        {
            for (var i = 0; i <= normalizedSource.Length - wSize; i++)
            {
                var window = normalizedSource.Substring(i, wSize);
                var distance = ComputeLevenshteinDistance(normalizedCandidate, window);
                bestDistance = Math.Min(bestDistance, distance);

                // Early exit on exact match
                if (bestDistance == 0)
                    return 1.0;
            }
        }

        if (bestDistance == int.MaxValue)
            return 0.0;

        var similarity = 1.0 - (double)bestDistance / normalizedCandidate.Length;
        return Math.Max(0.0, similarity);
    }

    public bool IsMatch(string candidate, string source, double threshold = 0.8)
    {
        return ComputeSimilarity(candidate, source) >= threshold;
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Lowercase
        var lower = text.ToLowerInvariant();

        // Collapse whitespace to single spaces and trim
        var chars = new char[lower.Length];
        var writeIndex = 0;
        var lastWasSpace = true; // Treat start as space to trim leading

        for (var i = 0; i < lower.Length; i++)
        {
            if (char.IsWhiteSpace(lower[i]))
            {
                if (!lastWasSpace)
                {
                    chars[writeIndex++] = ' ';
                    lastWasSpace = true;
                }
            }
            else
            {
                chars[writeIndex++] = lower[i];
                lastWasSpace = false;
            }
        }

        // Trim trailing space
        if (writeIndex > 0 && chars[writeIndex - 1] == ' ')
            writeIndex--;

        return new string(chars, 0, writeIndex);
    }

    private static int ComputeLevenshteinDistance(string a, string b)
    {
        var aLen = a.Length;
        var bLen = b.Length;

        if (aLen == 0) return bLen;
        if (bLen == 0) return aLen;

        // Use two-row optimization to reduce memory from O(n*m) to O(min(n,m))
        if (aLen < bLen)
        {
            (a, b) = (b, a);
            (aLen, bLen) = (bLen, aLen);
        }

        var previousRow = new int[bLen + 1];
        var currentRow = new int[bLen + 1];

        for (var j = 0; j <= bLen; j++)
            previousRow[j] = j;

        for (var i = 1; i <= aLen; i++)
        {
            currentRow[0] = i;

            for (var j = 1; j <= bLen; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                currentRow[j] = Math.Min(
                    Math.Min(currentRow[j - 1] + 1, previousRow[j] + 1),
                    previousRow[j - 1] + cost);
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[bLen];
    }
}
