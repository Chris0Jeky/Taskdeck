namespace Taskdeck.Domain.Enums;

/// <summary>
/// Discrete confidence tiers derived from a 0.0–1.0 continuous score.
/// Boundaries: [0, 0.2) = VeryLow, [0.2, 0.4) = Low, [0.4, 0.6) = Medium,
/// [0.6, 0.8) = High, [0.8, 1.0] = VeryHigh.
/// </summary>
public enum ConfidenceBucket
{
    VeryLow = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    VeryHigh = 4
}
