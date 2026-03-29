using System.Text.Json;

namespace Taskdeck.Application.Services;

/// <summary>
/// Shared system prompt and response parser for LLM-assisted instruction extraction.
/// Used by OpenAI and Gemini providers to extract structured instructions from
/// natural language chat messages.
/// </summary>
public static class LlmInstructionExtractionPrompt
{
    /// <summary>
    /// System prompt that instructs the LLM to extract actionable board instructions
    /// from natural language. The LLM should respond in JSON format.
    /// </summary>
    public const string SystemPrompt = """
        You are Taskdeck, a board-management assistant. Analyze the user's message and respond with valid JSON only.

        Supported instruction patterns (use these exact forms):
        - create card '<title>' [in column '<column>']
        - move card <id> to column '<column>'
        - archive card <id>
        - archive cards matching '<pattern>'
        - update card <id> title '<new title>'
        - update card <id> description '<new description>'
        - rename board to '<name>'
        - move column '<name>' to position <n>

        Respond with this JSON structure:
        {
          "reply": "<your conversational response to the user>",
          "actionable": true|false,
          "instructions": ["<instruction 1>", "<instruction 2>"]
        }

        Rules:
        - Set "actionable" to true only if the user is requesting a concrete board action.
        - Each entry in "instructions" must follow one of the supported instruction patterns exactly.
        - If the message is not actionable, set "instructions" to an empty array.
        - If you cannot map the request to a supported pattern, set "actionable" to false and explain in "reply".
        - Never invent card IDs. If the user references a card by name but you do not have its ID, use create card instead of update/move/archive.
        - Do not include any text outside the JSON object.
        """;

    /// <summary>
    /// Attempts to parse a structured instruction-extraction response from an LLM.
    /// Returns true if the JSON was valid and contained the expected shape.
    /// </summary>
    public static bool TryParseStructuredResponse(
        string responseBody,
        out string reply,
        out bool actionable,
        out List<string> instructions)
    {
        reply = string.Empty;
        actionable = false;
        instructions = new List<string>();

        if (string.IsNullOrWhiteSpace(responseBody))
            return false;

        try
        {
            // Strip markdown code fences if present (LLMs sometimes wrap JSON)
            var trimmed = responseBody.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = trimmed.IndexOf('\n');
                if (firstNewline >= 0)
                    trimmed = trimmed[(firstNewline + 1)..];
                if (trimmed.EndsWith("```", StringComparison.Ordinal))
                    trimmed = trimmed[..^3].TrimEnd();
            }

            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            if (root.TryGetProperty("reply", out var replyEl))
                reply = replyEl.GetString() ?? string.Empty;

            if (root.TryGetProperty("actionable", out var actionableEl))
                actionable = actionableEl.ValueKind == JsonValueKind.True;

            if (root.TryGetProperty("instructions", out var instructionsEl) &&
                instructionsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in instructionsEl.EnumerateArray())
                {
                    var instruction = item.GetString();
                    if (!string.IsNullOrWhiteSpace(instruction))
                        instructions.Add(instruction);
                }
            }

            // Require at least a reply to consider the parse successful
            return !string.IsNullOrWhiteSpace(reply);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
