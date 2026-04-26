namespace Taskdeck.Application.DTOs;

/// <summary>
/// A single hour bucket in the cadence response.
/// </summary>
public sealed record CadenceBucketDto(int Hour, int EventCount);

/// <summary>
/// API response DTO for daily cadence aggregation.
/// Contains 24 per-hour buckets and first/peak/last action metadata.
/// </summary>
public sealed record CadenceResponse(
    IReadOnlyList<CadenceBucketDto> Buckets,
    DateTimeOffset? FirstActionAt,
    int? PeakHour,
    DateTimeOffset? LastActionAt);
