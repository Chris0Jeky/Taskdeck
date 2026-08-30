namespace Taskdeck.Domain.Enums;

/// <summary>
/// What a unit of understanding is (ADR-0065 §Decision 5). This is a different axis from the
/// ADR-0060 work-item types (Task, Epic, Spike): only <see cref="Action"/> candidates compile into
/// proposal operations today; the other kinds are records worth keeping and reviewing.
/// </summary>
public enum SemanticCandidateKind
{
    Action = 0,
    Decision = 1,
    Question = 2,
    Risk = 3,
    Fact = 4,
    Reference = 5
}
