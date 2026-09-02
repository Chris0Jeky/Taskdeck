namespace Taskdeck.AccelerationCandidates;

public static class EvidenceValidators
{
    public static void ValidateTimeRange(long startMs, long endMs, long representationDurationMs)
    {
        if (startMs < 0 || endMs <= startMs || endMs > representationDurationMs)
            throw new ArgumentOutOfRangeException(nameof(startMs), "Evidence time range must be half-open and inside the representation duration.");
    }

    public static void ValidateNormalizedRegion(double x, double y, double width, double height)
    {
        var values = new[] { x, y, width, height };
        if (values.Any(v => double.IsNaN(v) || double.IsInfinity(v)) || x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > 1 || y + height > 1)
            throw new ArgumentOutOfRangeException(nameof(x), "Region must be a positive rectangle inside normalized 0..1 coordinates.");
    }
}
