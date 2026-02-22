using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class NoOpNotificationService : INotificationService
{
    public static readonly NoOpNotificationService Instance = new();

    private NoOpNotificationService()
    {
    }

    public Task<Result<IEnumerable<NotificationDto>>> GetNotificationsAsync(
        Guid userId,
        NotificationQueryDto query,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success<IEnumerable<NotificationDto>>(Array.Empty<NotificationDto>()));
    }

    public Task<Result<NotificationDto>> MarkAsReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<NotificationDto>(ErrorCodes.NotFound, "Notification not found"));
    }

    public Task<Result<NotificationPreferenceDto>> GetPreferencesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(Result.Success(new NotificationPreferenceDto(
            userId,
            true,
            true,
            false,
            true,
            false,
            true,
            false,
            now,
            now)));
    }

    public Task<Result<NotificationPreferenceDto>> UpdatePreferencesAsync(
        Guid userId,
        UpdateNotificationPreferenceDto dto,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(Result.Success(new NotificationPreferenceDto(
            userId,
            dto.InAppChannelEnabled,
            dto.MentionImmediateEnabled,
            dto.MentionDigestEnabled,
            dto.AssignmentImmediateEnabled,
            dto.AssignmentDigestEnabled,
            dto.ProposalOutcomeImmediateEnabled,
            dto.ProposalOutcomeDigestEnabled,
            now,
            now)));
    }

    public Task<Result<bool>> PublishAsync(
        CreateNotificationRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success(false));
    }
}
