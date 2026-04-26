using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.DTOs;

/// <summary>
/// DTO representing a single row in the card history ledger for the proposal review History section.
/// </summary>
public record CardHistoryRowDto(
    string Serial,
    string Event,
    string Age,
    CardHistoryStatus Status);
