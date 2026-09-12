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

public sealed class BoardCardRelationsApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task GetRelations_RequiresReadAccessAndKeepsArchivedBoardsReadableButNotWritable()
    {
        using var ownerClient = factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "relations-owner");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(ownerClient, "Relations board");
        using var viewerClient = factory.CreateClient();
        var viewer = await ApiTestHarness.AuthenticateAsync(viewerClient, "relations-viewer");
        using var outsiderClient = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(outsiderClient, "relations-outsider");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            db.BoardAccesses.Add(new BoardAccess(boardId, viewer.UserId, UserRole.Viewer, owner.UserId));
            await db.SaveChangesAsync();
        }

        var route = $"/api/boards/{boardId}/relations";
        using var anonymousClient = factory.CreateClient();
        (await anonymousClient.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await outsiderClient.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var ownerResult = (await ownerClient.GetFromJsonAsync<BoardRelationsDto>(route))!;
        ownerResult.BoardId.Should().Be(boardId);
        ownerResult.Revision.Should().Be(0);
        ownerResult.Relations.Should().BeEmpty();
        ownerResult.CanWrite.Should().BeTrue();
        (await viewerClient.GetFromJsonAsync<BoardRelationsDto>(route))!.CanWrite.Should().BeFalse();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Boards.SingleAsync(board => board.Id == boardId)).Archive();
            await db.SaveChangesAsync();
        }

        var archived = (await ownerClient.GetFromJsonAsync<BoardRelationsDto>(route))!;
        archived.Relations.Should().BeEmpty();
        archived.CanWrite.Should().BeFalse();
    }
}
