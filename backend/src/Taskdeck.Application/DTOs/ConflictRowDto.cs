using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.DTOs;

/// <summary>
/// DTO for a single conflict/warning/status row returned by the conflict detector.
/// </summary>
public record ConflictRowDto(
    ConflictTone Tone,
    string Key,
    string Value
)
{
    public static ConflictRowDto FromDomain(ConflictRow row)
    {
        return new ConflictRowDto(row.Tone, row.Key, row.Value);
    }
}
