namespace Taskdeck.Domain.Enums;

/// <summary>
/// The kind of principal that produced a capture (ADR-0065 §Decision 2, amended 2026-08-30).
/// Always server-stamped from the authenticated context (GP-02); never taken from client input.
/// An import is a transport (<see cref="CaptureOriginAdapter.Import"/>), not a principal: a human
/// or an integration performs it, so there is no <c>Import</c> producer. The producing principal's
/// identity, when it is not the owner, is <c>Capture.ProducedByPrincipalId</c>.
/// </summary>
public enum CaptureProducerKind
{
    Human = 0,
    Agent = 1,
    Integration = 2
}
