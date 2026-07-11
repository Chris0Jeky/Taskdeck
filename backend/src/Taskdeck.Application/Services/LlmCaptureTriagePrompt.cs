using System.Text.Json;
using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Services;

/// <summary>
/// System prompt and response parser for LLM-backed transcript triage (REVIVAL-08 M1).
/// The prompt demands a minimal JSON object (<c>{"tasks":[{"title","evidence"}]}</c>); the
/// versioned envelope (<see cref="CaptureTriageOutputContract.PromptVersionLlmV1"/>) is
/// constructed server-side so the model is never trusted with contract constants.
/// Evidence must be quoted verbatim from the transcript so evidence spans (REVIVAL-09) can be
/// recovered later by exact-substring search against the raw text.
/// </summary>
public static class LlmCaptureTriagePrompt
{
    /// <summary>
    /// Bumping the extraction prompt in a way that changes output semantics requires a new
    /// prompt-version constant in <see cref="CaptureTriageOutputContract"/> and a matching schema
    /// file, so recorded provenance stays attributable to the prompt that actually ran.
    /// </summary>
    public const string PromptVersion = CaptureTriageOutputContract.PromptVersionLlmV1;

    public const string SystemPrompt = """
        You are Taskdeck's transcript triage engine. Extract concrete action items from the transcript in the user message.

        Respond with a single JSON object of this exact shape and nothing else:
        {"tasks":[{"title":"...","evidence":"..."}]}

        Rules:
        - Output raw JSON only: no markdown fences, no commentary, no fields other than "tasks", "title", "evidence".
        - Extract between 1 and 20 tasks. Prefer fewer, higher-confidence items over exhaustive lists.
        - "title": the action item rephrased as a short imperative instruction (who should do what), at most 180 characters.
        - "evidence": a short VERBATIM quote copied character-for-character from the transcript that justifies the task, at most 280 characters. Never paraphrase, translate, or correct the quote.
        - Only include genuine action items: commitments, assignments, decisions requiring follow-up, or explicit next steps.
        - Ignore greetings, small talk, status recaps, and general discussion that requires no action.
        - Never invent tasks, names, or dates that the transcript does not support.
        - If the transcript contains no actionable items at all, respond with {"tasks":[]}.
        """;

    /// <summary>
    /// Leniently parses an LLM completion into raw (title, evidence) pairs. Tolerates markdown code
    /// fences and prose around the object via brace matching (the same shape
    /// <c>LlmInstructionExtractionPrompt.TryParseStructuredResponse</c> relies on) and ignores any
    /// extra JSON properties the model added. Returns false when no well-formed <c>tasks</c> array
    /// is present. EVERY array element yields an entry — malformed elements (non-objects, missing or
    /// non-string fields) yield blank fields for the caller's sanitization to drop — so a non-empty
    /// array can never masquerade as the deliberate "no action items" verdict that an
    /// empty-but-present array represents (the two have different failure semantics downstream:
    /// fallback vs honest empty).
    /// </summary>
    public static bool TryParseTasks(string? content, out List<CaptureTriageTaskV1> tasks)
    {
        tasks = new List<CaptureTriageTaskV1>();

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
                !root.TryGetProperty("tasks", out var tasksElement) ||
                tasksElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in tasksElement.EnumerateArray())
            {
                var title = string.Empty;
                var evidence = string.Empty;
                if (item.ValueKind == JsonValueKind.Object)
                {
                    if (item.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String)
                    {
                        title = titleEl.GetString() ?? string.Empty;
                    }

                    if (item.TryGetProperty("evidence", out var evidenceEl) && evidenceEl.ValueKind == JsonValueKind.String)
                    {
                        evidence = evidenceEl.GetString() ?? string.Empty;
                    }
                }

                tasks.Add(new CaptureTriageTaskV1(title, evidence));
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
