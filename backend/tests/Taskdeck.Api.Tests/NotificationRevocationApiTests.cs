using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class NotificationRevocationApiTests
{
    [Fact]
    public async Task RevokedBoardNotifications_AreHiddenFromListsAndDirectReadReceipts()
    {
        using var factory = new HostedWorkerDisabledTestWebApplicationFactory();
        using var ownerClient = factory.CreateClient();
        using var memberClient = factory.CreateClient();
        using var outsiderClient = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(ownerClient, "notice-owner");
        var member = await ApiTestHarness.AuthenticateAsync(memberClient, "notice-member");
        await ApiTestHarness.AuthenticateAsync(outsiderClient, "notice-outsider");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "notice-private");
        var grant = await ownerClient.PostAsJsonAsync($"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, member.UserId, UserRole.Viewer));
        grant.StatusCode.Should().Be(HttpStatusCode.OK);
        var access = await grant.Content.ReadFromJsonAsync<BoardAccessDto>();
        access.Should().NotBeNull();

        var scoped = new Notification(member.UserId, NotificationType.Mention,
            NotificationCadence.Immediate, "Revocation canary title", "Revocation canary private message", board.Id);
        var boardless = new Notification(member.UserId, NotificationType.System,
            NotificationCadence.Immediate, "Account notice", "Synthetic boardless notice");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            db.Notifications.AddRange(scoped, boardless);
            await db.SaveChangesAsync();
        }

        var before = await memberClient.GetFromJsonAsync<List<NotificationDto>>("/api/notifications");
        before.Should().Contain(item => item.Id == scoped.Id);
        before.Should().Contain(item => item.Id == boardless.Id);
        var revoke = await ownerClient.DeleteAsync($"/api/boards/{board.Id}/access/{access!.Id}");
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await memberClient.GetAsync("/api/notifications");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(scoped.Title).And.NotContain(scoped.Message);
        var after = await response.Content.ReadFromJsonAsync<List<NotificationDto>>();
        after.Should().NotContain(item => item.BoardId == board.Id);
        after.Should().Contain(item => item.Id == boardless.Id);
        await ApiTestHarness.AssertForbiddenAsync(
            await memberClient.GetAsync($"/api/notifications?boardId={board.Id}"));

        var markRead = await memberClient.PostAsync($"/api/notifications/{scoped.Id}/read", null);
        await ApiTestHarness.AssertForbiddenAsync(markRead);
        var markBody = await markRead.Content.ReadAsStringAsync();
        markBody.Should().NotContain(scoped.Title).And.NotContain(scoped.Message);
        await ApiTestHarness.AssertForbiddenAsync(
            await outsiderClient.PostAsync($"/api/notifications/{boardless.Id}/read", null));
        var accountRead = await memberClient.PostAsync($"/api/notifications/{boardless.Id}/read", null);
        accountRead.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var persisted = await db.Notifications.AsNoTracking().SingleAsync(item => item.Id == scoped.Id);
            persisted.IsRead.Should().BeFalse();
        }

        // Re-grant is resolved on the next read; no stale denial/grant cache survives.
        var regrant = await ownerClient.PostAsJsonAsync($"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, member.UserId, UserRole.Viewer));
        regrant.StatusCode.Should().Be(HttpStatusCode.OK);
        var restored = await memberClient.GetFromJsonAsync<List<NotificationDto>>("/api/notifications");
        restored.Should().Contain(item => item.Id == scoped.Id);
        var restoredRead = await memberClient.PostAsync($"/api/notifications/{scoped.Id}/read", null);
        restoredRead.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
