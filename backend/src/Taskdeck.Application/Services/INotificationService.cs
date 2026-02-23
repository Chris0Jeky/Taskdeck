using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface INotificationService
{
    Task<Result<IEnumerable<NotificationDto>>> GetNotificationsAsync(
        Guid userId,
        NotificationQueryDto query,
        CancellationToken cancellationToken = default);

    Task<Result<NotificationDto>> MarkAsReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default);

    Task<Result<NotificationPreferenceDto>> GetPreferencesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<NotificationPreferenceDto>> UpdatePreferencesAsync(
        Guid userId,
        UpdateNotificationPreferenceDto dto,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> PublishAsync(
        CreateNotificationRequestDto dto,
        CancellationToken cancellationToken = default);
}
