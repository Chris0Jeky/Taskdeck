from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)


path = Path("backend/src/Taskdeck.Application/Services/LlmCaptureTriageExtractor.cs")
text = path.read_text(encoding="utf-8")

text = replace_once(
    text,
    """        var reduced = new List<CaptureTriageTaskV2>();
        var reducedSpans = new List<(int Start, int End)?>();
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfUnique(CaptureTriageTaskV2 task)
        {
            if (reduced.Count < CaptureTriageOutputContract.MaxTasks && seenTitles.Add(task.Title))
            {
                reduced.Add(task);
                reducedSpans.Add(ResolveConsensusSpan(
                    sanitizedByChunk
                        .SelectMany(tasks => tasks)
                        .Where(candidate => string.Equals(candidate.Task.Title, task.Title, StringComparison.OrdinalIgnoreCase))
                        .Select(candidate => candidate.Span)));
            }
        }
""",
    """        var reduced = new List<CaptureTriageTaskV2>();
        var reducedSpans = new List<(int Start, int End)?>();
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var acceptedEvidenceOrigin = new Dictionary<(int Start, int End), int>();

        void AddIfUnique(MappedTask candidate, int chunkIndex)
        {
            if (reduced.Count >= CaptureTriageOutputContract.MaxTasks ||
                seenTitles.Contains(candidate.Task.Title))
            {
                return;
            }

            // Evidence ranges are absolute source coordinates. The same non-null range
            // appearing in two map chunks can only come from their deliberate overlap,
            // so keep the first stable task even when the model rephrases its title.
            // Within one chunk, however, one quote may legitimately support multiple
            // distinct commitments; null spans are ambiguous and are never dedupe keys.
            if (candidate.Span is { } span &&
                acceptedEvidenceOrigin.TryGetValue(span, out var originChunkIndex) &&
                originChunkIndex != chunkIndex)
            {
                return;
            }

            seenTitles.Add(candidate.Task.Title);
            reduced.Add(candidate.Task);
            reducedSpans.Add(ResolveConsensusSpan(
                sanitizedByChunk
                    .SelectMany(tasks => tasks)
                    .Where(existing => string.Equals(
                        existing.Task.Title,
                        candidate.Task.Title,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(existing => existing.Span)));

            if (candidate.Span is { } acceptedSpan)
            {
                acceptedEvidenceOrigin.TryAdd(acceptedSpan, chunkIndex);
            }
        }
""",
    "reducer admission",
)

text = replace_once(
    text,
    """                    AddIfUnique(sanitizedByChunk[chunkIndex][0].Task);
""",
    """                    AddIfUnique(sanitizedByChunk[chunkIndex][0], chunkIndex);
""",
    "coverage admission",
)

text = replace_once(
    text,
    """            foreach (var tasks in sanitizedByChunk)
            {
                if (taskIndex < tasks.Count)
                {
                    AddIfUnique(tasks[taskIndex].Task);
                }

                if (reduced.Count >= CaptureTriageOutputContract.MaxTasks)
                {
                    break;
                }
            }
""",
    """            for (var chunkIndex = 0; chunkIndex < sanitizedByChunk.Count; chunkIndex++)
            {
                var tasks = sanitizedByChunk[chunkIndex];
                if (taskIndex < tasks.Count)
                {
                    AddIfUnique(tasks[taskIndex], chunkIndex);
                }

                if (reduced.Count >= CaptureTriageOutputContract.MaxTasks)
                {
                    break;
                }
            }
""",
    "round-robin admission",
)

path.write_text(text, encoding="utf-8")
