namespace Taskdeck.Domain.Enums;

/// <summary>
/// Identifies the product surface that initiated an LLM call.
/// </summary>
public enum LlmSurface
{
    Chat = 0,
    CaptureTriage = 1,
    Worker = 2
}
