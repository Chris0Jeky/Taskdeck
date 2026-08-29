using System.Globalization;
using System.Text.Json;
using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Services;

/// <summary>
/// System prompt and response parser for LLM-backed transcript triage (REVIVAL-08 M3).
/// The server stamps the versioned envelope so the model cannot choose contract constants.
/// Evidence quotes are required to be verbatim so REVIVAL-09 can later link exact spans.
/// </summary>
public static class LlmCaptureTriagePrompt
{
    private static readonly HashSet<string> AllowedRootProperties = new(StringComparer.Ordinal)
    {
        "tasks"
    };

    private static readonly HashSet<string> AllowedTaskProperties = new(StringComparer.Ordinal)
    {
        "title",
        "type",
        "assigneeHint",
        "dueDateHint",
        "confidence",
        "evidenceQuote"
    };

    /// <summary>
    /// Bumping the extraction prompt in a way that changes output semantics requires a new
    /// prompt-version constant in <see cref="CaptureTriageOutputContract"/> and a matching schema
    /// file, so recorded provenance stays attributable to the prompt that actually ran.
    /// </summary>
    public const string PromptVersion = CaptureTriageOutputContract.PromptVersionLlmV2;

    /// <summary>
    /// Token the reference date replaces in the prompt template. It is exactly as long as a
    /// rendered <c>yyyy-MM-dd</c> date, so the template and every rendered prompt have the same
    /// length and a token/size estimate taken from either one is exact.
    /// </summary>
    public const string ReferenceDatePlaceholder = "{REF_DATE}";

    /// <summary>
    /// The unrendered instruction block. Private on purpose: sending it verbatim would ship the
    /// placeholder to the model, so callers go through <see cref="BuildSystemPrompt"/> or
    /// <see cref="SystemPrompt"/>.
    /// </summary>
    private const string SystemPromptTemplate = """
        You are Taskdeck's transcript triage engine. Extract concrete action items from the transcript in the user message.

        The transcript was captured on {REF_DATE}. That is the reference date, and it is the only date you know: resolve every date you emit against it and never assume a different year.

        Respond with a single JSON object of this exact shape and nothing else:
        {"tasks":[{"title":"...","type":"action","assigneeHint":null,"dueDateHint":null,"confidence":0.0,"evidenceQuote":"..."}]}

        Rules:
        - Output raw JSON only: no markdown fences, no commentary, no fields other than "tasks", "title", "type", "assigneeHint", "dueDateHint", "confidence", "evidenceQuote".
        - Extract between 1 and 20 tasks. Prefer fewer, higher-confidence items over exhaustive lists.
        - "title": the action item rephrased as a short imperative instruction (who should do what), at most 180 characters.
        - "type": exactly one of "action", "decision", or "question". Use "action" for a commitment or next step, "decision" for a decision worth recording, and "question" for an unresolved question that needs follow-up.
        - "assigneeHint": the explicitly named person responsible, or null when the transcript does not identify one. It is only a hint: never infer a Taskdeck user ID.
        - "dueDateHint": a calendar date in YYYY-MM-DD form, or null.
          - When the transcript states a day and month with no year (for example "Monday 1 September"), resolve it to the first such date on or after the reference date {REF_DATE}. Never guess or invent a year.
          - Do not calculate relative dates such as "next Friday" or "in two weeks": return null for those.
          - Return null whenever you cannot pin the item to one exact calendar day.
          - Never emit a date more than 2 years before or more than 5 years after the reference date; return null instead.
        - "confidence": your confidence from 0 through 1 that this item is supported by the evidence quote. It informs later review only and never authorizes a board write.
        - "evidenceQuote": a short VERBATIM quote copied character-for-character from the transcript that justifies the item, at most 280 characters. Never paraphrase, translate, or correct the quote.
        - Ignore greetings, small talk, status recaps, and general discussion that requires no action.
        - Never invent tasks, names, or dates that the transcript does not support.
        - If the transcript contains no actionable items at all, respond with {"tasks":[]}.
        """;

    /// <summary>
    /// The server's current UTC calendar day. ADR-0058 makes a due date a calendar day rather than
    /// an instant, so what the model resolves against is a day, not a timestamp.
    /// </summary>
    public static DateOnly CurrentReferenceDate => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>
    /// The prompt as it is sent when the caller holds no capture day of its own: the template
    /// rendered against <see cref="CurrentReferenceDate"/>. A capture is triaged within seconds to
    /// minutes of being created, and the plausibility window enforced by
    /// <see cref="CaptureTriageOutputContract.MaxDueDateYearsBeforeReference"/> /
    /// <see cref="CaptureTriageOutputContract.MaxDueDateYearsAfterReference"/> is years wide, so
    /// that drift cannot change an outcome. A caller holding the capture's own day should pass it
    /// to <see cref="BuildSystemPrompt"/> instead.
    /// </summary>
    public static string SystemPrompt => BuildSystemPrompt(CurrentReferenceDate);

    /// <summary>
    /// Renders the extraction prompt for one capture day (#2193). Without a reference date the
    /// model has no year to resolve "Monday 1 September" against, and a shipped run silently
    /// produced 2023-09-01 for a transcript spoken in August 2026.
    /// </summary>
    public static string BuildSystemPrompt(DateOnly referenceDate) =>
        SystemPromptTemplate.Replace(
            ReferenceDatePlaceholder,
            referenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

    /// <summary>
    /// Parses a schema-v2 LLM completion against the server's current UTC day
    /// (<see cref="CurrentReferenceDate"/>) and discards the notes. This is the shape the live
    /// extraction leg uses; see the four-argument overload for the reference date and notes.
    /// </summary>
    public static bool TryParseTasks(string? content, out List<CaptureTriageTaskV2> tasks) =>
        TryParseTasks(content, CurrentReferenceDate, out tasks, out _);

    /// <summary>
    /// Parses a schema-v2 LLM completion while tolerating surrounding prose/fences via brace
    /// matching. It rejects unknown, missing, and wrongly typed fields rather than silently
    /// normalizing untrusted model metadata. An empty-but-present array remains the deliberate
    /// "no action items" verdict; malformed non-empty content instead triggers fallback.
    /// <para>
    /// A due-date hint is the one field that is dropped rather than rejected (#2193). It is a hint
    /// on a reviewable proposal, so an unusable date must not cost the caller every extracted task
    /// — but it must never reach a card either. A hint that is not a <c>yyyy-MM-dd</c> calendar
    /// date, or that falls outside the plausibility window around <paramref name="referenceDate"/>,
    /// is nulled and reported in <paramref name="notes"/> instead of being carried forward.
    /// </para>
    /// </summary>
    public static bool TryParseTasks(
        string? content,
        DateOnly referenceDate,
        out List<CaptureTriageTaskV2> tasks,
        out IReadOnlyList<string> notes)
    {
        tasks = new List<CaptureTriageTaskV2>();
        var collectedNotes = new List<string>();
        notes = collectedNotes;

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var trimmed = content.Trim();
        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace)
        {
            return false;
        }

        trimmed = trimmed[firstBrace..(lastBrace + 1)];

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !HasExactPropertySet(root, AllowedRootProperties) ||
                !root.TryGetProperty("tasks", out var tasksElement) ||
                tasksElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in tasksElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !HasExactPropertySet(item, AllowedTaskProperties) ||
                    !TryGetRequiredString(item, "title", out var title) ||
                    !TryGetRequiredString(item, "type", out var type) ||
                    !TryGetRequiredNullableString(item, "assigneeHint", out var assigneeHint) ||
                    !TryGetRequiredNullableString(item, "dueDateHint", out var dueDateHint) ||
                    !item.TryGetProperty("confidence", out var confidenceElement) ||
                    confidenceElement.ValueKind != JsonValueKind.Number ||
                    !confidenceElement.TryGetDecimal(out var confidence) ||
                    !TryGetRequiredString(item, "evidenceQuote", out var evidenceQuote))
                {
                    tasks.Clear();
                    collectedNotes.Clear();
                    return false;
                }

                var reviewedDueDateHint = CaptureTriageOutputContract.ReviewDueDateHint(
                    dueDateHint,
                    tasks.Count + 1,
                    referenceDate,
                    out var dueDateNote);
                if (dueDateNote is not null)
                {
                    collectedNotes.Add(dueDateNote);
                }

                tasks.Add(new CaptureTriageTaskV2(
                    title,
                    type,
                    assigneeHint,
                    reviewedDueDateHint,
                    confidence,
                    evidenceQuote));
            }

            return true;
        }
        catch (JsonException)
        {
            tasks.Clear();
            collectedNotes.Clear();
            return false;
        }
    }

    private static bool HasExactPropertySet(JsonElement item, HashSet<string> expectedProperties)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in item.EnumerateObject())
        {
            if (!expectedProperties.Contains(property.Name) || !seen.Add(property.Name))
            {
                return false;
            }
        }

        return seen.Count == expectedProperties.Count;
    }

    private static bool TryGetRequiredString(JsonElement item, string propertyName, out string value)
    {
        value = string.Empty;
        if (!item.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetRequiredNullableString(JsonElement item, string propertyName, out string? value)
    {
        value = null;
        if (!item.TryGetProperty(propertyName, out var element) ||
            (element.ValueKind != JsonValueKind.String && element.ValueKind != JsonValueKind.Null))
        {
            return false;
        }

        value = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        return true;
    }
}
