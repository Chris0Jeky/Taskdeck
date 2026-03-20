namespace Taskdeck.Application.Services;

internal static class CaptureConversionTimestamp
{
    internal static DateTimeOffset ResolveConvertedAt(DateTime? appliedAt)
    {
        if (!appliedAt.HasValue)
        {
            return DateTimeOffset.UtcNow;
        }

        var normalized = appliedAt.Value.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(appliedAt.Value, DateTimeKind.Utc),
            DateTimeKind.Local => appliedAt.Value.ToUniversalTime(),
            _ => appliedAt.Value
        };

        return new DateTimeOffset(normalized);
    }
}
