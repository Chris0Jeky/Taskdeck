using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class NotificationService : INotificationService
{
    private const int MaxNotificationListLimit = 500;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthorizationService? _authorizationService;

    public NotificationService(
        IUnitOfWork unitOfWork,
        IAuthorizationService? authorizationService = null)
    {
        _unitOfWork = unitOfWork;
        _authorizationService = authorizationService;
    }

    public async Task<Result<IEnumerable<NotificationDto>>> GetNotificationsAsync(
        Guid userId,
        NotificationQueryDto query,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<IEnumerable<NotificationDto>>(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (query.Limit <= 0 || query.Limit > MaxNotificationListLimit)
        {
            return Result.Failure<IEnumerable<NotificationDto>>(
                ErrorCodes.ValidationError,
                $"Limit must be between 1 and {MaxNotificationListLimit}");
        }

        if (query.BoardId.HasValue && _authorizationService is not null)
        {
            var boardPermission = await _authorizationService.CanReadBoardAsync(userId, query.BoardId.Value);
            if (!boardPermission.IsSuccess)
                return Result.Failure<IEnumerable<NotificationDto>>(boardPermission.ErrorCode, boardPermission.ErrorMessage);

            if (!boardPermission.Value)
            {
                return Result.Failure<IEnumerable<NotificationDto>>(
                    ErrorCodes.Forbidden,
                    "You do not have access to notifications for this board");
            }
        }

        var notifications = await _unitOfWork.Notifications.GetByUserIdAsync(
            userId,
            query.Limit,
            query.UnreadOnly,
            query.BoardId,
            cancellationToken);

        return Result.Success(notifications.Select(MapToDto));
    }

    public async Task<Result<NotificationDto>> MarkAsReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<NotificationDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (notificationId == Guid.Empty)
            return Result.Failure<NotificationDto>(ErrorCodes.ValidationError, "Notification ID cannot be empty");

        var notification = await _unitOfWork.Notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null)
            return Result.Failure<NotificationDto>(ErrorCodes.NotFound, $"Notification with ID {notificationId} not found");

        if (notification.UserId != userId)
            return Result.Failure<NotificationDto>(ErrorCodes.Forbidden, "You do not have access to this notification");

        notification.MarkAsRead();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(MapToDto(notification));
    }

    public async Task<Result<NotificationPreferenceDto>> GetPreferencesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<NotificationPreferenceDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        var preference = await EnsurePreferenceAsync(userId, cancellationToken);
        return Result.Success(MapPreferenceToDto(preference));
    }

    public async Task<Result<NotificationPreferenceDto>> UpdatePreferencesAsync(
        Guid userId,
        UpdateNotificationPreferenceDto dto,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<NotificationPreferenceDto>(ErrorCodes.ValidationError, "User ID cannot be empty");

        var preference = await EnsurePreferenceAsync(userId, cancellationToken);
        preference.Update(
            dto.InAppChannelEnabled,
            dto.MentionImmediateEnabled,
            dto.MentionDigestEnabled,
            dto.AssignmentImmediateEnabled,
            dto.AssignmentDigestEnabled,
            dto.ProposalOutcomeImmediateEnabled,
            dto.ProposalOutcomeDigestEnabled);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(MapPreferenceToDto(preference));
    }

    public async Task<Result<bool>> PublishAsync(
        CreateNotificationRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var preference = await EnsurePreferenceAsync(dto.UserId, cancellationToken, persistOnCreate: false);
            if (!preference.InAppChannelEnabled)
                return Result.Success(false);

            if (!TryResolveCadence(preference, dto.Type, out var cadence))
                return Result.Success(false);

            if (!string.IsNullOrWhiteSpace(dto.DeduplicationKey))
            {
                var existing = await _unitOfWork.Notifications.GetByUserAndDeduplicationKeyAsync(
                    dto.UserId,
                    dto.DeduplicationKey,
                    cancellationToken);

                if (existing is not null)
                    return Result.Success(false);
            }

            var notification = new Notification(
                dto.UserId,
                dto.Type,
                cadence,
                dto.Title,
                dto.Message,
                dto.BoardId,
                dto.SourceEntityType,
                dto.SourceEntityId,
                dto.DeduplicationKey);

            await _unitOfWork.Notifications.AddAsync(notification, cancellationToken);
            return Result.Success(true);
        }
        catch (DomainException ex)
        {
            return Result.Failure<bool>(ex.ErrorCode, ex.Message);
        }
    }

    private async Task<NotificationPreference> EnsurePreferenceAsync(
        Guid userId,
        CancellationToken cancellationToken,
        bool persistOnCreate = true)
    {
        var preference = await _unitOfWork.NotificationPreferences.GetByUserIdAsync(userId, cancellationToken);
        if (preference is not null)
            return preference;

        preference = NotificationPreference.CreateDefault(userId);
        await _unitOfWork.NotificationPreferences.AddAsync(preference, cancellationToken);
        if (persistOnCreate)
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        return preference;
    }

    private static bool TryResolveCadence(
        NotificationPreference preference,
        NotificationType type,
        out NotificationCadence cadence)
    {
        cadence = NotificationCadence.Immediate;

        var immediateEnabled = type switch
        {
            NotificationType.Mention => preference.MentionImmediateEnabled,
            NotificationType.Assignment => preference.AssignmentImmediateEnabled,
            NotificationType.ProposalOutcome => preference.ProposalOutcomeImmediateEnabled,
            _ => false
        };

        var digestEnabled = type switch
        {
            NotificationType.Mention => preference.MentionDigestEnabled,
            NotificationType.Assignment => preference.AssignmentDigestEnabled,
            NotificationType.ProposalOutcome => preference.ProposalOutcomeDigestEnabled,
            _ => false
        };

        if (!immediateEnabled && !digestEnabled)
            return false;

        cadence = immediateEnabled ? NotificationCadence.Immediate : NotificationCadence.Digest;
        return true;
    }

    private static NotificationDto MapToDto(Notification notification)
    {
        return new NotificationDto(
            notification.Id,
            notification.UserId,
            notification.BoardId,
            notification.Type,
            notification.Cadence,
            notification.Title,
            notification.Message,
            notification.SourceEntityType,
            notification.SourceEntityId,
            notification.IsRead,
            notification.ReadAt,
            notification.CreatedAt,
            notification.UpdatedAt);
    }

    private static NotificationPreferenceDto MapPreferenceToDto(NotificationPreference preference)
    {
        return new NotificationPreferenceDto(
            preference.UserId,
            preference.InAppChannelEnabled,
            preference.MentionImmediateEnabled,
            preference.MentionDigestEnabled,
            preference.AssignmentImmediateEnabled,
            preference.AssignmentDigestEnabled,
            preference.ProposalOutcomeImmediateEnabled,
            preference.ProposalOutcomeDigestEnabled,
            preference.CreatedAt,
            preference.UpdatedAt);
    }
}
