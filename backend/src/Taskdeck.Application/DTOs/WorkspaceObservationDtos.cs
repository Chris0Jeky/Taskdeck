using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.DTOs;

public record ObservationSourceDto(Guid CardId, string Title, string Text, string Fingerprint, bool Truncated);
public record GenerateObservationsDto(Guid BoardId, Guid CardId, [Required, StringLength(64, MinimumLength = 64)] string Fingerprint);
public record ObservationCandidate(string Kind, string Question, string Reason, string Quote);
public record ObservationEvidence(string Fingerprint, string Quote, string SourceTitle, DateTimeOffset GeneratedAt, string Provider, string Model);
