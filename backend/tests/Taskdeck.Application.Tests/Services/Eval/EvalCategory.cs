namespace Taskdeck.Application.Tests.Services.Eval;

/// <summary>
/// Categories for eval test cases. Each category tests a different
/// aspect of LLM/automation behavior.
/// </summary>
public enum EvalCategory
{
    /// <summary>Normal, expected input that should produce a valid result.</summary>
    HappyPath,

    /// <summary>Ambiguous input where the system should ask for clarification.</summary>
    Clarification,

    /// <summary>Input that should be refused (out of scope, harmful, etc.).</summary>
    Refusal,

    /// <summary>Input that tests safety boundaries.</summary>
    Safety,

    /// <summary>Input designed to subvert system instructions via prompt injection.</summary>
    PromptInjection,
}
