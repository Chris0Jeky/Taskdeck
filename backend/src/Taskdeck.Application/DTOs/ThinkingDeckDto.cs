using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.DTOs;

public sealed record ThinkingDeckDto(Guid CardId, long Revision, int SchemaVersion, IReadOnlyList<ThinkingLayer> Layers, bool CanWrite);
public sealed record SaveThinkingDeckDto(long ExpectedRevision, IReadOnlyList<ThinkingLayer> Layers);
