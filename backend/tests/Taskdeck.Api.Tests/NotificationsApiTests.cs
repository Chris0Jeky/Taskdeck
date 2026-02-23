using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class NotificationsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public NotificationsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetNotifications_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/notifications");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task PreferencesEndpoints_ShouldGetAndUpdateCurrentUserPreferences()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "notifications-preferences");

        var getResponse = await client.GetAsync("/api/notifications/preferences");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var preferences = await getResponse.Content.ReadFromJsonAsync<NotificationPreferenceDto>();
        preferences.Should().NotBeNull();
        preferences!.InAppChannelEnabled.Should().BeTrue();
        preferences.MentionImmediateEnabled.Should().BeTrue();

        var updateDto = new UpdateNotificationPreferenceDto(
            InAppChannelEnabled: true,
            MentionImmediateEnabled: false,
            MentionDigestEnabled: true,
            AssignmentImmediateEnabled: true,
            AssignmentDigestEnabled: false,
            ProposalOutcomeImmediateEnabled: false,
            ProposalOutcomeDigestEnabled: true);

        var updateResponse = await client.PutAsJsonAsync("/api/notifications/preferences", updateDto);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await updateResponse.Content.ReadFromJsonAsync<NotificationPreferenceDto>();
        updated.Should().NotBeNull();
        updated!.MentionImmediateEnabled.Should().BeFalse();
        updated.MentionDigestEnabled.Should().BeTrue();
        updated.ProposalOutcomeImmediateEnabled.Should().BeFalse();
        updated.ProposalOutcomeDigestEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task MarkAsRead_ShouldReturnForbidden_ForCrossUserNotification()
    {
        using var ownerClient = _factory.CreateClient();
        using var otherClient = _factory.CreateClient();

        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "notifications-owner");
        await ApiTestHarness.AuthenticateAsync(otherClient, "notifications-other");

        var notification = await SeedNotificationAsync(owner.UserId);

        var response = await otherClient.PostAsync($"/api/notifications/{notification.Id}/read", content: null);

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task MarkAsRead_ShouldUpdateReadState_ForOwner()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "notifications-read");
        var notification = await SeedNotificationAsync(user.UserId);

        var response = await client.PostAsync($"/api/notifications/{notification.Id}/read", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<NotificationDto>();
        payload.Should().NotBeNull();
        payload!.IsRead.Should().BeTrue();
        payload.ReadAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetNotifications_WithForeignBoardFilter_ShouldReturnForbidden()
    {
        using var ownerClient = _factory.CreateClient();
        using var otherClient = _factory.CreateClient();

        _ = await ApiTestHarness.AuthenticateAsync(ownerClient, "notifications-board-owner");
        await ApiTestHarness.AuthenticateAsync(otherClient, "notifications-board-other");
        var ownerBoard = await ApiTestHarness.CreateBoardAsync(ownerClient, "notifications-board");

        var response = await otherClient.GetAsync($"/api/notifications?boardId={ownerBoard.Id}&unreadOnly=true");

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task SaveChanges_ShouldIgnoreConcurrentNotificationDeduplicationConflicts()
    {
        var userId = Guid.NewGuid();
        using (var seedScope = _factory.Services.CreateScope())
        {
            var dbContext = seedScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var userSuffix = Guid.NewGuid().ToString("N")[..8];
            var user = new User($"dedupe-{userSuffix}", $"dedupe-{userSuffix}@example.com", "hash");
            dbContext.Users.Add(user);
            dbContext.NotificationPreferences.Add(NotificationPreference.CreateDefault(user.Id));
            await dbContext.SaveChangesAsync();
            userId = user.Id;
        }

        using var scopeA = _factory.Services.CreateScope();
        using var scopeB = _factory.Services.CreateScope();
        var notificationServiceA = scopeA.ServiceProvider.GetRequiredService<INotificationService>();
        var notificationServiceB = scopeB.ServiceProvider.GetRequiredService<INotificationService>();
        var unitOfWorkA = scopeA.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var unitOfWorkB = scopeB.ServiceProvider.GetRequiredService<IUnitOfWork>();

        const string deduplicationKey = "race-deduplication-key";
        var request = new CreateNotificationRequestDto(
            userId,
            NotificationType.Mention,
            "Concurrent notification",
            "Concurrent dedupe test",
            DeduplicationKey: deduplicationKey);

        var publishResultA = await notificationServiceA.PublishAsync(request);
        var publishResultB = await notificationServiceB.PublishAsync(request);

        publishResultA.IsSuccess.Should().BeTrue();
        publishResultA.Value.Should().BeTrue();
        publishResultB.IsSuccess.Should().BeTrue();
        publishResultB.Value.Should().BeTrue();

        await unitOfWorkA.SaveChangesAsync();
        var saveB = async () => await unitOfWorkB.SaveChangesAsync();
        await saveB.Should().NotThrowAsync();

        using var verifyScope = _factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var duplicateCount = verifyContext.Notifications.Count(notification =>
            notification.UserId == userId && notification.DeduplicationKey == deduplicationKey);

        duplicateCount.Should().Be(1);
    }

    private async Task<Notification> SeedNotificationAsync(Guid userId, Guid? boardId = null)
    {
        var notification = new Notification(
            userId,
            NotificationType.Mention,
            NotificationCadence.Immediate,
            "Test notification",
            "This is a seeded notification.",
            boardId,
            sourceEntityType: "test",
            sourceEntityId: Guid.NewGuid(),
            deduplicationKey: $"seed:{Guid.NewGuid():N}");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();
        return notification;
    }
}
