namespace Taskdeck.Domain.Enums;

/// <summary>
/// The kind of principal that produced a capture (ADR-0065 §Decision 2). Always server-stamped from
/// the authenticated context (GP-02); never taken from client input.
/// </summary>
public enum CaptureProducerKind
{
    Human = 0,
    Agent = 1,
    Integration = 2,
    Import = 3
}
