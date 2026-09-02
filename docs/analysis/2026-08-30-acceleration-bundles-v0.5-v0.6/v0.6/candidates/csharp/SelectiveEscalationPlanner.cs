
namespace Taskdeck.Acceleration.V06;

public enum EscalationAnchorKind
{
    TextSpan = 0,
    TimeRange = 1,
    PageRegion = 2,
    ImageRegion = 3
}

public sealed record EscalationAnchor(
    Guid RepresentationId,
    EscalationAnchorKind Kind,
    int? CharStart,
    int? CharEnd,
    long? StartMs,
    long? EndMs,
    int? Page,
    double? X,
    double? Y,
    double? Width,
    double? Height,
    string ReasonCode);

public sealed record EscalationPlan(
    Guid ParentRepresentationId,
    string Capability,
    string TargetProcessorId,
    IReadOnlyList<EscalationAnchor> Anchors,
    bool FullRerunFallbackRequired);

public static class SelectiveEscalationPlanner
{
    public static EscalationPlan Create(
        Guid parentRepresentationId,
        string capability,
        string targetProcessorId,
        IReadOnlyList<EscalationAnchor> anchors,
        bool targetSupportsPartialInput)
    {
        if (parentRepresentationId == Guid.Empty) throw new ArgumentException("parentRepresentationId");
        if (string.IsNullOrWhiteSpace(capability)) throw new ArgumentException("capability");
        if (string.IsNullOrWhiteSpace(targetProcessorId)) throw new ArgumentException("targetProcessorId");
        if (anchors.Count == 0) throw new ArgumentException("anchors");
        if (!targetSupportsPartialInput)
            throw new InvalidOperationException("processing.escalation.unsupported");

        foreach (var anchor in anchors)
        {
            if (anchor.RepresentationId != parentRepresentationId)
                throw new InvalidOperationException("processing.escalation.mixed-parent");
            ValidateAnchor(anchor);
        }

        return new EscalationPlan(
            parentRepresentationId,
            capability,
            targetProcessorId,
            anchors.OrderBy(AnchorOrder).ToList(),
            FullRerunFallbackRequired: true);
    }

    private static double AnchorOrder(EscalationAnchor anchor) =>
        anchor.CharStart ?? anchor.StartMs ?? anchor.Page ?? 0;

    private static void ValidateAnchor(EscalationAnchor anchor)
    {
        if (string.IsNullOrWhiteSpace(anchor.ReasonCode))
            throw new InvalidOperationException("processing.escalation.reason-required");
        if (anchor.CharStart is < 0 || anchor.CharEnd < anchor.CharStart)
            throw new InvalidOperationException("processing.escalation.text-range-invalid");
        if (anchor.StartMs is < 0 || anchor.EndMs < anchor.StartMs)
            throw new InvalidOperationException("processing.escalation.time-range-invalid");
        if (anchor.X is not null &&
            (anchor.X < 0 || anchor.Y < 0 || anchor.Width <= 0 || anchor.Height <= 0 ||
             anchor.X + anchor.Width > 1 || anchor.Y + anchor.Height > 1))
            throw new InvalidOperationException("processing.escalation.region-invalid");
    }
}
