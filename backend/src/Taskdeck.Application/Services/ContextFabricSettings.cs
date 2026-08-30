namespace Taskdeck.Application.Services;

/// <summary>
/// Settings for the Context Fabric migration (ADR-0065), bound from the <c>ContextFabric</c>
/// configuration section. Every flag here is a compatibility switch for a bounded slice; none
/// changes shipped behaviour while it keeps its default.
/// </summary>
public sealed class ContextFabricSettings
{
    /// <summary>
    /// When true, every capture created through <c>CaptureService.CreateAsync</c> is mirrored into
    /// the durable <c>Captures</c> table with the queue row's id (ID-preserving dual-write,
    /// CF-01 <c>#2255</c>). Inbox reads keep using the queue row until CF-01 completes the backfill
    /// and flips the read path. Default false: the table stays empty on an unchanged install.
    /// </summary>
    public bool DualWriteCaptures { get; set; }
}
