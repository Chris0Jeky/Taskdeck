using System.Text.RegularExpressions;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

/// <summary>
/// Detects whether an LLM response is a clarification question (asking the user
/// for more detail before generating instructions) versus a direct answer or
/// instruction set. Also tracks clarification round counts from message history.
/// </summary>
public static class ClarificationDetector
{
    /// <summary>Maximum clarification rounds before the LLM should attempt best-effort.</summary>
    public const int MaxClarificationRounds = 2;

    /// <summary>
    /// Phrases that indicate the user wants to skip clarification and proceed
    /// with best-effort generation.
    /// </summary>
    private static readonly string[] SkipPhrases =
    {
        "just do your best",
        "do your best",
        "skip clarification",
        "just go ahead",
        "go ahead",
        "best guess",
        "figure it out",
        "your best guess",
        "whatever you think",
        "just do it",
        "skip questions"
    };

    /// <summary>
    /// Strong clarification patterns — these are reliable indicators that the
    /// response is asking for clarification even without a question mark.
    /// </summary>
    private static readonly Regex StrongClarificationPattern = new(
        @"(?:" +
            // Numbered list of questions (1. ... 2. ...)
            @"(?:\d+\.\s+.+\?\s*){2,}" +
            @"|" +
            // "Could you tell me" / "Can you clarify" / "I need to know more" patterns
            @"(?:could you (?:tell|clarify|specify|let me know)|can you (?:clarify|specify|tell me)|i need more (?:details|information))" +
            @"|" +
            // "I'd like to ask" / "let me ask" — explicit ask intent
            @"(?:i'd like to (?:ask|clarify)|let me ask)" +
        @")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>
    /// Weak clarification patterns — these phrases appear in both clarification
    /// questions and normal action statements, so they require at least one
    /// question mark in the content to be considered clarification.
    /// </summary>
    private static readonly Regex WeakClarificationPattern = new(
        @"(?:" +
            // "Before I can" / "To help you better" / "I'd like to know" / "I need to know"
            @"(?:before i can|to help you (?:better|with)|i'd like to know|i need to know|(?:please|could you) (?:provide|share|give me))" +
        @")",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>
    /// Additional heuristic: content has question marks suggesting inquiry.
    /// We require at least 2 question marks in the response to avoid false
    /// positives on single rhetorical questions.
    /// </summary>
    private static readonly Regex MultiQuestionPattern = new(
        @"\?\s", RegexOptions.Compiled);

    /// <summary>
    /// Determines if the LLM response content is a clarification question.
    /// Returns true when the response is asking the user for more information
    /// rather than providing a direct answer or instruction set.
    /// </summary>
    public static bool IsClarificationResponse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        // Strong patterns are reliable on their own — no question mark required
        if (StrongClarificationPattern.IsMatch(content))
            return true;

        // Count question marks (both mid-text and trailing)
        var questionCount = MultiQuestionPattern.Matches(content).Count;
        if (content.TrimEnd().EndsWith('?'))
            questionCount++;

        // Weak patterns only count as clarification when the response
        // actually contains a question mark. This prevents false positives on
        // normal responses like "To help you with this, I created the cards."
        // or "Before I can proceed, let me create the cards."
        if (questionCount > 0 && WeakClarificationPattern.IsMatch(content))
            return true;

        // Heuristic: multiple question marks suggest clarification
        return questionCount >= 2;
    }

    /// <summary>
    /// Determines if the user message is requesting to skip clarification.
    /// </summary>
    public static bool IsSkipRequest(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return false;

        var normalized = userMessage.Trim().ToLowerInvariant();
        return SkipPhrases.Any(phrase => normalized.Contains(phrase, StringComparison.Ordinal));
    }

    /// <summary>
    /// Counts the number of consecutive clarification rounds at the end of the
    /// message history. A clarification round is a pair of (assistant clarification
    /// message, user response).
    /// </summary>
    public static int CountClarificationRounds(IReadOnlyList<ChatMessage> messages)
    {
        var rounds = 0;

        // Walk backwards through messages looking for clarification patterns.
        // Each round consists of an assistant clarification message followed by
        // a user response.
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];

            if (msg.Role == ChatMessageRole.User)
            {
                // Check if the previous message was a clarification
                if (i > 0 && messages[i - 1].Role == ChatMessageRole.Assistant
                          && messages[i - 1].MessageType == "clarification")
                {
                    rounds++;
                    i--; // Skip the assistant message we just checked
                    continue;
                }
            }

            // If we hit a non-clarification assistant message or any other pattern, stop
            break;
        }

        return rounds;
    }

    /// <summary>
    /// Determines if clarification has been exhausted (max rounds reached)
    /// and the system should attempt best-effort instruction generation.
    /// </summary>
    public static bool ShouldForceBestEffort(IReadOnlyList<ChatMessage> messages)
    {
        return CountClarificationRounds(messages) >= MaxClarificationRounds;
    }

    /// <summary>
    /// Builds a system prompt suffix that instructs the LLM to ask clarifying
    /// questions when intent is ambiguous, or to generate best-effort
    /// instructions when clarification has been exhausted.
    /// </summary>
    public static string BuildClarificationSystemPrompt(int currentRounds, bool forcebestEffort)
    {
        if (forcebestEffort)
        {
            return "\n\nIMPORTANT: You have already asked clarifying questions. " +
                   "Do NOT ask any more questions. Generate your best-effort instructions " +
                   "based on the information provided so far. Make reasonable assumptions " +
                   "for any missing details.";
        }

        return "\n\nWhen the user's request is actionable but ambiguous (e.g., missing " +
               "details like how many items, which column, specific names), ask clarifying " +
               "questions instead of guessing. Format your questions as a numbered list. " +
               "Keep questions concise and relevant. " +
               $"You have asked {currentRounds} of {MaxClarificationRounds} allowed clarification rounds.";
    }
}
