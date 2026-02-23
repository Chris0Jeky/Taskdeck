using System.Text.RegularExpressions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class CardCommentService
{
    private static readonly Regex MentionRegex = new(@"(?<![A-Za-z0-9_.-])@(?<username>[A-Za-z0-9_.-]{3,50})", RegexOptions.Compiled);

    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;
    private readonly IAuthorizationService? _authorizationService;

    public CardCommentService(
        IUnitOfWork unitOfWork,
        INotificationService? notificationService = null,
        IAuthorizationService? authorizationService = null)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService ?? NoOpNotificationService.Instance;
        _authorizationService = authorizationService;
    }

    public async Task<Result<IEnumerable<CardCommentDto>>> GetCommentsAsync(
        Guid boardId,
        Guid cardId,
        CancellationToken cancellationToken = default)
    {
        var cardResult = await EnsureCardBelongsToBoardAsync(boardId, cardId, cancellationToken);
        if (!cardResult.IsSuccess)
            return Result.Failure<IEnumerable<CardCommentDto>>(cardResult.ErrorCode, cardResult.ErrorMessage);

        var comments = await _unitOfWork.CardComments.GetByCardIdAsync(cardId, cancellationToken);
        return Result.Success(comments.Select(MapToDto));
    }

    public async Task<Result<CardCommentDto>> CreateCommentAsync(
        Guid boardId,
        Guid cardId,
        Guid actorUserId,
        CreateCardCommentDto dto,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
            return Result.Failure<CardCommentDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (string.IsNullOrWhiteSpace(dto.Content))
            return Result.Failure<CardCommentDto>(ErrorCodes.ValidationError, "Comment content cannot be empty");

        var cardResult = await EnsureCardBelongsToBoardAsync(boardId, cardId, cancellationToken);
        if (!cardResult.IsSuccess)
            return Result.Failure<CardCommentDto>(cardResult.ErrorCode, cardResult.ErrorMessage);

        if (dto.ParentCommentId.HasValue)
        {
            var parentResult = await EnsureParentCommentAsync(cardId, dto.ParentCommentId.Value, cancellationToken);
            if (!parentResult.IsSuccess)
                return Result.Failure<CardCommentDto>(parentResult.ErrorCode, parentResult.ErrorMessage);
        }

        var actor = await _unitOfWork.Users.GetByIdAsync(actorUserId, cancellationToken);
        if (actor is null)
            return Result.Failure<CardCommentDto>(ErrorCodes.NotFound, $"User with ID {actorUserId} not found");

        try
        {
            var comment = new CardComment(cardId, boardId, actorUserId, dto.Content, dto.ParentCommentId);
            await RefreshMentionsAsync(comment, dto.Content, actorUserId, boardId, cancellationToken);

            await _unitOfWork.CardComments.AddAsync(comment, cancellationToken);
            await _unitOfWork.AuditLogs.AddAsync(
                new AuditLog(
                    "card-comment",
                    comment.Id,
                    AuditAction.Created,
                    actorUserId,
                    $"card_id={cardId};parent_comment_id={dto.ParentCommentId?.ToString() ?? "none"}"),
                cancellationToken);

            var publishResult = await PublishMentionNotificationsAsync(
                comment,
                actor.Username,
                cardResult.Value.Title,
                cancellationToken);
            if (!publishResult.IsSuccess)
                return Result.Failure<CardCommentDto>(publishResult.ErrorCode, publishResult.ErrorMessage);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var created = await _unitOfWork.CardComments.GetByIdWithMentionsAsync(comment.Id, cancellationToken);
            return Result.Success(MapToDto(created ?? comment));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CardCommentDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<CardCommentDto>> UpdateCommentAsync(
        Guid boardId,
        Guid cardId,
        Guid commentId,
        Guid actorUserId,
        UpdateCardCommentDto dto,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
            return Result.Failure<CardCommentDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (string.IsNullOrWhiteSpace(dto.Content))
            return Result.Failure<CardCommentDto>(ErrorCodes.ValidationError, "Comment content cannot be empty");

        var comment = await _unitOfWork.CardComments.GetByIdWithMentionsAsync(commentId, cancellationToken);
        if (comment is null || comment.CardId != cardId || comment.BoardId != boardId)
        {
            return Result.Failure<CardCommentDto>(
                ErrorCodes.NotFound,
                $"Comment with ID {commentId} not found for card {cardId}");
        }

        var moderatorCheck = await CanModerateCommentAsync(comment, actorUserId, boardId, cancellationToken);
        if (!moderatorCheck.IsSuccess)
            return Result.Failure<CardCommentDto>(moderatorCheck.ErrorCode, moderatorCheck.ErrorMessage);

        if (!moderatorCheck.Value)
            return Result.Failure<CardCommentDto>(ErrorCodes.Forbidden, "You do not have access to modify this comment");

        var cardResult = await EnsureCardBelongsToBoardAsync(boardId, cardId, cancellationToken);
        if (!cardResult.IsSuccess)
            return Result.Failure<CardCommentDto>(cardResult.ErrorCode, cardResult.ErrorMessage);

        var actor = await _unitOfWork.Users.GetByIdAsync(actorUserId, cancellationToken);
        if (actor is null)
            return Result.Failure<CardCommentDto>(ErrorCodes.NotFound, $"User with ID {actorUserId} not found");

        try
        {
            var existingMentionUserIds = comment.Mentions
                .Select(mention => mention.MentionedUserId)
                .ToHashSet();

            comment.UpdateContent(dto.Content);
            await RefreshMentionsAsync(comment, dto.Content, actorUserId, boardId, cancellationToken);

            await _unitOfWork.AuditLogs.AddAsync(
                new AuditLog(
                    "card-comment",
                    comment.Id,
                    AuditAction.Updated,
                    actorUserId,
                    $"card_id={cardId};is_deleted={comment.IsDeleted}"),
                cancellationToken);

            var mentionUsersToNotify = comment.Mentions
                .Where(mention => !existingMentionUserIds.Contains(mention.MentionedUserId))
                .ToList();

            var publishResult = await PublishMentionNotificationsAsync(
                comment,
                actor.Username,
                cardResult.Value.Title,
                cancellationToken,
                mentionUsersToNotify);
            if (!publishResult.IsSuccess)
                return Result.Failure<CardCommentDto>(publishResult.ErrorCode, publishResult.ErrorMessage);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var updated = await _unitOfWork.CardComments.GetByIdWithMentionsAsync(comment.Id, cancellationToken);
            return Result.Success(MapToDto(updated ?? comment));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CardCommentDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result> DeleteCommentAsync(
        Guid boardId,
        Guid cardId,
        Guid commentId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
            return Result.Failure(ErrorCodes.ValidationError, "User ID cannot be empty");

        var comment = await _unitOfWork.CardComments.GetByIdWithMentionsAsync(commentId, cancellationToken);
        if (comment is null || comment.CardId != cardId || comment.BoardId != boardId)
        {
            return Result.Failure(
                ErrorCodes.NotFound,
                $"Comment with ID {commentId} not found for card {cardId}");
        }

        var moderatorCheck = await CanModerateCommentAsync(comment, actorUserId, boardId, cancellationToken);
        if (!moderatorCheck.IsSuccess)
            return Result.Failure(moderatorCheck.ErrorCode, moderatorCheck.ErrorMessage);

        if (!moderatorCheck.Value)
            return Result.Failure(ErrorCodes.Forbidden, "You do not have access to delete this comment");

        comment.SoftDelete();
        await _unitOfWork.AuditLogs.AddAsync(
            new AuditLog(
                "card-comment",
                comment.Id,
                AuditAction.Deleted,
                actorUserId,
                $"card_id={cardId}"),
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Result<Card>> EnsureCardBelongsToBoardAsync(
        Guid boardId,
        Guid cardId,
        CancellationToken cancellationToken)
    {
        var card = await _unitOfWork.Cards.GetByIdAsync(cardId, cancellationToken);
        if (card is null || card.BoardId != boardId)
        {
            return Result.Failure<Card>(
                ErrorCodes.NotFound,
                $"Card with ID {cardId} not found in board {boardId}");
        }

        return Result.Success(card);
    }

    private async Task<Result<CardComment>> EnsureParentCommentAsync(
        Guid cardId,
        Guid parentCommentId,
        CancellationToken cancellationToken)
    {
        var parent = await _unitOfWork.CardComments.GetByIdAsync(parentCommentId, cancellationToken);
        if (parent is null || parent.CardId != cardId)
        {
            return Result.Failure<CardComment>(
                ErrorCodes.NotFound,
                $"Parent comment with ID {parentCommentId} not found for card {cardId}");
        }

        if (parent.ParentCommentId.HasValue)
        {
            return Result.Failure<CardComment>(
                ErrorCodes.ValidationError,
                "Replies to replies are not supported");
        }

        return Result.Success(parent);
    }

    private async Task RefreshMentionsAsync(
        CardComment comment,
        string content,
        Guid actorUserId,
        Guid boardId,
        CancellationToken cancellationToken)
    {
        var usernames = MentionRegex
            .Matches(content)
            .Select(match => match.Groups["username"].Value)
            .Where(username => !string.IsNullOrWhiteSpace(username))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (usernames.Length == 0)
        {
            comment.ReplaceMentions(Array.Empty<(Guid userId, string username)>());
            return;
        }

        var mentionTargets = new List<(Guid userId, string username)>();
        foreach (var username in usernames)
        {
            var mentionedUser = await _unitOfWork.Users.GetByUsernameAsync(username, cancellationToken);
            if (mentionedUser is null || mentionedUser.Id == actorUserId)
                continue;

            if (_authorizationService is not null)
            {
                var canRead = await _authorizationService.CanReadBoardAsync(mentionedUser.Id, boardId);
                if (!canRead.IsSuccess || !canRead.Value)
                    continue;
            }

            mentionTargets.Add((mentionedUser.Id, mentionedUser.Username));
        }

        comment.ReplaceMentions(mentionTargets);
    }

    private async Task<Result<bool>> CanModerateCommentAsync(
        CardComment comment,
        Guid actorUserId,
        Guid boardId,
        CancellationToken cancellationToken)
    {
        if (comment.AuthorUserId == actorUserId)
            return Result.Success(true);

        if (_authorizationService is null)
            return Result.Success(false);

        var roleResult = await _authorizationService.GetUserRoleForBoardAsync(actorUserId, boardId);
        if (!roleResult.IsSuccess)
            return Result.Failure<bool>(roleResult.ErrorCode, roleResult.ErrorMessage);

        var role = roleResult.Value;
        var canModerate = role == UserRole.Owner || role == UserRole.Admin;
        return Result.Success(canModerate);
    }

    private async Task<Result> PublishMentionNotificationsAsync(
        CardComment comment,
        string actorName,
        string cardTitle,
        CancellationToken cancellationToken,
        IEnumerable<CardCommentMention>? mentionsOverride = null)
    {
        var mentionTargets = mentionsOverride?.ToArray() ?? comment.Mentions.ToArray();
        foreach (var mention in mentionTargets)
        {
            var publishResult = await _notificationService.PublishAsync(
                new CreateNotificationRequestDto(
                    mention.MentionedUserId,
                    NotificationType.Mention,
                    "You were mentioned in a card comment",
                    $"{actorName} mentioned you on card '{cardTitle}'.",
                    comment.BoardId,
                    SourceEntityType: "card-comment",
                    SourceEntityId: comment.Id,
                    DeduplicationKey: $"mention:card-comment:{comment.Id}:{mention.MentionedUserId}"),
                cancellationToken);

            if (!publishResult.IsSuccess)
                return Result.Failure(publishResult.ErrorCode, publishResult.ErrorMessage);
        }

        return Result.Success();
    }

    private static CardCommentDto MapToDto(CardComment comment)
    {
        var authorUsername = comment.AuthorUser?.Username ?? "unknown";
        var mentions = comment.Mentions
            .Select(mention => new CardCommentMentionDto(
                mention.MentionedUserId,
                mention.MentionedUser?.Username ?? mention.MentionedUsername))
            .ToList();

        return new CardCommentDto(
            comment.Id,
            comment.BoardId,
            comment.CardId,
            comment.ParentCommentId,
            comment.AuthorUserId,
            authorUsername,
            comment.Content,
            comment.IsDeleted,
            comment.EditedAt,
            mentions,
            comment.CreatedAt,
            comment.UpdatedAt);
    }
}
