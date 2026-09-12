using Taskdeck.Domain.Entities;
namespace Taskdeck.Application.DTOs;
public sealed record BoardRelationsDto(Guid BoardId, long Revision, IReadOnlyList<CardRelationEdge> Relations, bool CanWrite);
