namespace Taskdeck.Domain.Enums;

/// <summary>
/// Why a reviewer flagged an automation proposal as a bad/unhelpful suggestion.
/// A closed set of structural categories only -- this is content-free negative feedback
/// for the learning loop and must never carry free text or other PII.
/// </summary>
public enum ProposalFeedbackReason
{
    /// <summary>No category supplied (the default one-click "Report" with an empty body).</summary>
    Unspecified = 0,

    /// <summary>The suggestion was not relevant to the user's intent.</summary>
    Irrelevant,

    /// <summary>The suggestion was factually wrong or would produce an incorrect change.</summary>
    Incorrect,

    /// <summary>The suggestion duplicated an existing card or change.</summary>
    Duplicate,

    /// <summary>The suggestion was too risky / destructive to apply.</summary>
    TooRisky,

    /// <summary>A reason outside the listed categories.</summary>
    Other
}
