using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class CardEstimateApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Estimate_RoundTripsUnknownZeroClearAndLegacyEdits_WithActorHistory()
    {
        using var client = factory.CreateClient();
        var (actor, board, column, card) = await SeedAsync(client);
        var path = $"/api/boards/{board.Id}/cards/{card.Id}";
        card.EstimatedEffortMinutes.Should().BeNull();
        (await client.PatchAsJsonAsync(path, new { estimatedEffortMinutes = 0 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PatchAsJsonAsync(path, new { clearEstimatedEffort = true })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var zeroResponse = await client.PatchAsJsonAsync(path, new { estimatedEffortMinutes = 0, expectedUpdatedAt = card.UpdatedAt });
        zeroResponse.EnsureSuccessStatusCode();
        var zero = (await zeroResponse.Content.ReadFromJsonAsync<CardDto>())!;
        zero.EstimatedEffortMinutes.Should().Be(0);
        zero.UpdatedAt.Should().BeAfter(card.UpdatedAt);
        var repeated = await client.PatchAsJsonAsync(path, new { estimatedEffortMinutes = 0, expectedUpdatedAt = zero.UpdatedAt });
        (await repeated.Content.ReadFromJsonAsync<CardDto>())!.UpdatedAt.Should().Be(zero.UpdatedAt);
        var ordinary = await client.PatchAsJsonAsync(path, new { title = "Legacy edit", estimatedEffortMinutes = (int?)null });
        var current = (await ordinary.Content.ReadFromJsonAsync<CardDto>())!;
        current.EstimatedEffortMinutes.Should().Be(0);
        var cleared = await client.PatchAsJsonAsync(path, new { clearEstimatedEffort = true, expectedUpdatedAt = current.UpdatedAt });
        cleared.EnsureSuccessStatusCode();
        (await cleared.Content.ReadFromJsonAsync<CardDto>())!.EstimatedEffortMinutes.Should().BeNull();
        var created = await client.PostAsJsonAsync($"/api/boards/{board.Id}/cards",
            new { columnId = column.Id, title = "Known estimate", estimatedEffortMinutes = 90 });
        created.EnsureSuccessStatusCode();
        (await created.Content.ReadFromJsonAsync<CardDto>())!.EstimatedEffortMinutes.Should().Be(90);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var audit = await db.AuditLogs.Where(a => a.EntityId == card.Id && a.Changes!.Contains("Estimated effort:")).ToListAsync();
        audit.Should().HaveCount(2);
        audit.Should().OnlyContain(a => a.UserId == actor.UserId && a.Action == AuditAction.Updated);
        audit.Select(a => a.Changes).Should().BeEquivalentTo("Estimated effort: unknown -> 0m", "Estimated effort: 0m -> unknown");
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(1_000_001, false)]
    [InlineData(60, true)]
    public async Task InvalidEstimate_DoesNotPersistOtherRequestedChanges(int minutes, bool clear)
    {
        using var client = factory.CreateClient();
        var (_, board, column, card) = await SeedAsync(client);
        var path = $"/api/boards/{board.Id}/cards/{card.Id}";
        (await client.PatchAsJsonAsync(path, new { title = "Must not change", estimatedEffortMinutes = minutes,
            clearEstimatedEffort = clear, expectedUpdatedAt = card.UpdatedAt })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var persisted = (await client.GetFromJsonAsync<CardDto>(path))!;
        persisted.Title.Should().Be(card.Title);
        persisted.EstimatedEffortMinutes.Should().BeNull();
        persisted.UpdatedAt.Should().Be(card.UpdatedAt);
        if (!clear)
            (await client.PostAsJsonAsync($"/api/boards/{board.Id}/cards", new { columnId = column.Id,
                title = "Invalid create", estimatedEffortMinutes = minutes })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{board.Id}/cards"))!.Should().ContainSingle();
    }

    [Fact]
    public async Task StaleEstimateWrite_CannotReplaceWinningEstimate()
    {
        using var client = factory.CreateClient();
        var (_, board, _, card) = await SeedAsync(client);
        var path = $"/api/boards/{board.Id}/cards/{card.Id}";
        (await client.PatchAsJsonAsync(path, new { estimatedEffortMinutes = 90, expectedUpdatedAt = card.UpdatedAt })).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync(path, new { estimatedEffortMinutes = 60, expectedUpdatedAt = card.UpdatedAt })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetFromJsonAsync<CardDto>(path))!.EstimatedEffortMinutes.Should().Be(90);
    }

    [Fact]
    public async Task EstimateWrite_RespectsBoardScopePermissionsAndArchiveGuards()
    {
        using var owner = factory.CreateClient();
        var (_, board, _, card) = await SeedAsync(owner);
        var path = $"/api/boards/{board.Id}/cards/{card.Id}";
        var patch = new { estimatedEffortMinutes = 90, expectedUpdatedAt = card.UpdatedAt };
        using var anonymous = factory.CreateClient();
        (await anonymous.PatchAsJsonAsync(path, patch)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var outsider = factory.CreateClient();
        var viewer = await ApiTestHarness.AuthenticateAsync(outsider, "effort-viewer");
        (await outsider.PatchAsJsonAsync(path, patch)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PostAsJsonAsync($"/api/boards/{board.Id}/access", new GrantAccessDto(board.Id, viewer.UserId, UserRole.Viewer))).EnsureSuccessStatusCode();
        (await outsider.PatchAsJsonAsync(path, patch)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var otherBoard = await ApiTestHarness.CreateBoardAsync(owner, "Other effort board");
        (await owner.PatchAsJsonAsync($"/api/boards/{otherBoard.Id}/cards/{card.Id}", patch)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var archive = await owner.PostAsJsonAsync(path + "/archive", new { expectedUpdatedAt = card.UpdatedAt });
        archive.EnsureSuccessStatusCode();
        var archived = (await archive.Content.ReadFromJsonAsync<CardDto>())!;
        (await owner.PatchAsJsonAsync(path, new { estimatedEffortMinutes = 90, expectedUpdatedAt = archived.UpdatedAt })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var restore = await owner.PostAsJsonAsync(path + "/restore", new { expectedUpdatedAt = archived.UpdatedAt });
        restore.EnsureSuccessStatusCode();
        var active = (await restore.Content.ReadFromJsonAsync<CardDto>())!;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Boards.FindAsync(board.Id))!.Archive();
            await db.SaveChangesAsync();
        }
        (await owner.PatchAsJsonAsync(path, new { estimatedEffortMinutes = 90, expectedUpdatedAt = active.UpdatedAt })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private static async Task<(TestUserContext Actor, BoardDto Board, ColumnDto Column, CardDto Card)> SeedAsync(HttpClient client)
    {
        var actor = await ApiTestHarness.AuthenticateAsync(client, "effort-owner");
        var board = await ApiTestHarness.CreateBoardAsync(client, "Effort board");
        var colResponse = await client.PostAsJsonAsync($"/api/boards/{board.Id}/columns", new CreateColumnDto(board.Id, "Next", 0, null));
        colResponse.EnsureSuccessStatusCode();
        var column = (await colResponse.Content.ReadFromJsonAsync<ColumnDto>())!;
        var cardResponse = await client.PostAsJsonAsync($"/api/boards/{board.Id}/cards", new { columnId = column.Id, title = "Legacy unknown" });
        cardResponse.EnsureSuccessStatusCode();
        return (actor, board, column, (await cardResponse.Content.ReadFromJsonAsync<CardDto>())!);
    }
}
