using Taskdeck.Domain.Entities;
namespace Taskdeck.Application.DTOs;

public sealed record BoardDependencyDto(Guid BoardId, long Revision, IReadOnlyList<CardDependency> Edges, bool CanWrite);
public sealed record SaveBoardDependenciesDto(long ExpectedRevision, IReadOnlyList<CardDependency> Edges);
