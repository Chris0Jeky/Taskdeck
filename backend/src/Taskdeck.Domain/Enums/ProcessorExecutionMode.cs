namespace Taskdeck.Domain.Enums;

/// <summary>
/// How a processor runs (ADR-0065 §Decision 10; manifest field <c>execution</c>).
/// </summary>
public enum ProcessorExecutionMode
{
    /// <summary>Deterministic .NET code inside the API process (plaintext, PdfPig, mock).</summary>
    InProcess = 0,

    /// <summary>A supervised child process speaking the Taskdeck Worker Protocol over stdio.</summary>
    Sidecar = 1,

    /// <summary>A network service behind the egress envelope (cloud STT, cloud vision).</summary>
    Remote = 2
}

/// <summary>
/// Where a processor's compute happens (manifest field <c>locality</c>). A <see cref="Local"/>
/// processor must declare <c>networkRequired: false</c>; the router treats locality as the primary
/// privacy constraint.
/// </summary>
public enum ProcessorLocality
{
    Local = 0,
    Remote = 1,
    Hybrid = 2
}

/// <summary>Manifest field <c>resources.gpu</c>.</summary>
public enum ProcessorGpuRequirement
{
    None = 0,
    Optional = 1,
    Required = 2
}

/// <summary>Manifest field <c>costModel.type</c>.</summary>
public enum ProcessorCostModelType
{
    FreeLocal = 0,
    ComputeTime = 1,
    PerMinute = 2,
    PerToken = 3,
    PerPage = 4,
    Custom = 5
}
