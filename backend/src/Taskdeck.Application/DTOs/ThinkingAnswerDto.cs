using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.DTOs;

public sealed record ThinkingAnswerDto(long ExpectedRevision, [Required, MaxLength(8000)] string Text, [Required] string Status);
public sealed record ThinkingAnswerSourceDto(Guid CardId, Guid LayerId, long DeckRevision);
