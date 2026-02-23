namespace Taskdeck.Application.DTOs;

public record CardCommentMentionDto(
    Guid UserId,
    string Username);

public record CardCommentDto(
    Guid Id,
    Guid BoardId,
    Guid CardId,
    Guid? ParentCommentId,
    Guid AuthorUserId,
    string AuthorUsername,
    string Content,
    bool IsDeleted,
    DateTimeOffset? EditedAt,
    List<CardCommentMentionDto> Mentions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateCardCommentDto(
    string Content,
    Guid? ParentCommentId = null);

public record UpdateCardCommentDto(
    string Content);
