namespace Taskdeck.Application.DTOs;

public sealed record TomorrowNoteResponse(
    Guid Id,
    DateOnly Date,
    string Text,
    DateTimeOffset UpdatedAt,
    DateTimeOffset CreatedAt);

public sealed record SaveTomorrowNoteRequest(
    DateOnly Date,
    string Text);
