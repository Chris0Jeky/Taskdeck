using System.Text;
using System.Text.RegularExpressions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Plans bounded transcript chunks for the capture-triage LLM. The planner preserves every source
/// character across its chunk ranges, prefers speaker-turn and blank-line cuts, and only hard-splits
/// unstructured text when there is no safe preferred boundary inside the configured input budget.
/// </summary>
public static class TranscriptTriageChunker
{
    private static readonly Regex SpeakerTurnPattern = new(
        @"^[ \t]*(?:\[[^\]\r\n]{1,80}\][ \t]*)?(?:speaker[ \t]*\d+|[A-Z][\p{L}\p{M}'-]*(?:[ \t]+[A-Z][\p{L}\p{M}'-]*){0,4})[ \t]*:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// Splits <paramref name="text"/> into chunks whose conservative input-token estimate fits the
    /// budget. The overlap is capped at one quarter of the input budget and never prevents forward
    /// progress; it gives a following chunk enough local context without making repeated input
    /// unbounded.
    /// </summary>
    public static IReadOnlyList<TranscriptTriageChunk> Chunk(
        string text,
        int maxInputTokens,
        int overlapTokens)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxInputTokens, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(overlapTokens);

        if (text.Length == 0)
        {
            return Array.Empty<TranscriptTriageChunk>();
        }

        var boundedOverlapTokens = Math.Min(overlapTokens, Math.Max(0, (maxInputTokens - 1) / 4));
        var preferredBoundaries = FindPreferredBoundaries(text);
        var chunks = new List<TranscriptTriageChunk>();
        var start = 0;
        var previousEnd = 0;

        while (start < text.Length)
        {
            var hardEnd = FindLargestEndWithinBudget(text, start, maxInputTokens);
            // When the next chunk starts inside the previous one for overlap, an earlier preferred
            // boundary is still inside its input window. Require meaningful new progress beyond
            // the carried overlap before choosing a preferred boundary; otherwise a boundary just
            // past the previous end produces tiny repeat chunks and eventually loses overlap.
            // Preferred boundaries improve forced splits, but must not turn an otherwise
            // under-budget transcript into multiple provider requests.
            var minimumPreferredEnd = start < previousEnd
                ? (int)Math.Min(text.Length, (long)previousEnd + (previousEnd - start))
                : previousEnd;
            var end = hardEnd == text.Length
                ? hardEnd
                : FindPreferredEnd(preferredBoundaries, minimumPreferredEnd, hardEnd) ?? hardEnd;
            end = AvoidSplittingSurrogatePair(text, start, end);

            if (end <= start)
            {
                end = Math.Min(start + 1, text.Length);
            }

            var chunkText = text[start..end];
            chunks.Add(new TranscriptTriageChunk(
                chunks.Count,
                start,
                chunkText,
                TranscriptTokenEstimator.EstimateTokens(chunkText)));

            if (end == text.Length)
            {
                break;
            }

            previousEnd = end;

            var nextStart = boundedOverlapTokens == 0
                ? end
                : FindOverlapStart(text, start, end, boundedOverlapTokens);

            // A short preferred-boundary chunk can be smaller than the overlap budget. Retain its
            // final character when possible rather than silently losing all context, but never
            // replay the entire chunk: the next start must still advance monotonically.
            if (nextStart <= start && end - start > 1)
            {
                nextStart = AvoidSplittingSurrogatePair(text, start, end - 1);
            }

            start = nextStart > start ? nextStart : end;
        }

        return chunks;
    }

    private static List<int> FindPreferredBoundaries(string text)
    {
        var boundaries = new List<int>();
        var lineStart = 0;

        while (lineStart < text.Length)
        {
            var newLineIndex = text.IndexOf('\n', lineStart);
            var lineEnd = newLineIndex < 0 ? text.Length : newLineIndex + 1;
            var lineContentEnd = lineEnd;
            while (lineContentEnd > lineStart &&
                   (text[lineContentEnd - 1] == '\r' || text[lineContentEnd - 1] == '\n'))
            {
                lineContentEnd--;
            }

            var line = text[lineStart..lineContentEnd];
            if (string.IsNullOrWhiteSpace(line))
            {
                AddBoundary(boundaries, lineEnd);
            }
            else if (lineStart > 0 && SpeakerTurnPattern.IsMatch(line))
            {
                AddBoundary(boundaries, lineStart);
            }

            lineStart = lineEnd;
        }

        return boundaries;
    }

    private static void AddBoundary(List<int> boundaries, int boundary)
    {
        if (boundaries.Count == 0 || boundaries[^1] != boundary)
        {
            boundaries.Add(boundary);
        }
    }

    private static int FindLargestEndWithinBudget(string text, int start, int maxInputTokens)
    {
        var low = start + 1;
        var high = text.Length;
        var best = start;

        while (low <= high)
        {
            var candidate = low + ((high - low) / 2);
            var estimate = TranscriptTokenEstimator.EstimateTokens(text[start..candidate]);
            if (estimate <= maxInputTokens)
            {
                best = candidate;
                low = candidate + 1;
            }
            else
            {
                high = candidate - 1;
            }
        }

        return best == start ? Math.Min(start + 1, text.Length) : best;
    }

    private static int? FindPreferredEnd(IReadOnlyList<int> boundaries, int minimumEndExclusive, int hardEnd)
    {
        int? preferredEnd = null;
        foreach (var boundary in boundaries)
        {
            if (boundary <= minimumEndExclusive)
            {
                continue;
            }

            if (boundary > hardEnd)
            {
                break;
            }

            preferredEnd = boundary;
        }

        return preferredEnd;
    }

    private static int FindOverlapStart(string text, int chunkStart, int chunkEnd, int overlapTokens)
    {
        var low = chunkStart;
        var high = chunkEnd;
        var best = chunkEnd;

        while (low <= high)
        {
            var candidate = low + ((high - low) / 2);
            var estimate = TranscriptTokenEstimator.EstimateTokens(text[candidate..chunkEnd]);
            if (estimate <= overlapTokens)
            {
                best = candidate;
                high = candidate - 1;
            }
            else
            {
                low = candidate + 1;
            }
        }

        return AvoidSplittingSurrogatePair(text, chunkStart, best);
    }

    private static int AvoidSplittingSurrogatePair(string text, int lowerBound, int boundary)
    {
        if (boundary > lowerBound &&
            boundary < text.Length &&
            char.IsHighSurrogate(text[boundary - 1]) &&
            char.IsLowSurrogate(text[boundary]))
        {
            return boundary - 1;
        }

        return boundary;
    }
}

/// <summary>
/// Cheap, deliberately conservative input-token bound for transcript triage. It counts each UTF-8
/// byte as a token rather than inferring tokens from word runs: one-character words, identifiers,
/// and encoded data can tokenize much more densely than ordinary prose. The bound intentionally
/// over-reserves and may select more map chunks, preserving the no-tokenizer-dependency design
/// without reopening quota admission for those inputs.
/// </summary>
public static class TranscriptTokenEstimator
{
    public static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return Encoding.UTF8.GetByteCount(text);
    }
}

public sealed record TranscriptTriageChunk(
    int Index,
    int Offset,
    string Text,
    int EstimatedTokens)
{
    public int EndOffset => Offset + Text.Length;
}
