using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class WorkspacePlanApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private const string Url = "/api/workspace/plan";
    private static readonly DateOnly Date = new(2026, 9, 10);

    [Fact]
    public async Task AllEndpointsRequireAuthentication()
    {
        using var client = factory.CreateClient();
        (await client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PutAsJsonAsync(Url, new SaveWorkspacePlanDto(0, []))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync(Url + "/focus", new FocusWorkspacePlanDto(0, Guid.NewGuid(), Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PlanFocusAndMakeRoomPersistWithoutChangingCard()
    {
        using var client = factory.CreateClient();
        var (board, card) = await Setup(client);
        var initial = (await client.GetFromJsonAsync<WorkspacePlanDto>(Url))!;
        initial.Revision.Should().Be(0);
        (await client.PutAsJsonAsync(Url, new SaveWorkspacePlanDto(0, [new(board.Id, card.Id, Date)]))).EnsureSuccessStatusCode();
        var before = DateTimeOffset.UtcNow;
        (await client.PostAsJsonAsync(Url + "/focus", new FocusWorkspacePlanDto(1, board.Id, card.Id))).EnsureSuccessStatusCode();
        var saved = (await client.GetFromJsonAsync<WorkspacePlanDto>(Url))!;
        saved.Revision.Should().Be(2);
        saved.Entries.Single().Title.Should().Be(card.Title);
        saved.LastWorked!.WorkedAt.Should().BeOnOrAfter(before);
        saved.LastWorked.Available.Should().BeTrue();
        (await client.PutAsJsonAsync(Url, new SaveWorkspacePlanDto(2, []))).EnsureSuccessStatusCode();
        var after = (await client.GetFromJsonAsync<WorkspacePlanDto>(Url))!;
        after.Entries.Should().BeEmpty();
        after.LastWorked.Should().BeEquivalentTo(saved.LastWorked);
        var actual = (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{board.Id}/cards"))!.Single();
        actual.Should().BeEquivalentTo(card, "personal planning must not mutate any card fields");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmptyFocusTargetDoesNotChangeSavedPlan(bool emptyBoard)
    {
        using var client = factory.CreateClient();
        var (board, card) = await Setup(client);
        (await client.PutAsJsonAsync(Url, new SaveWorkspacePlanDto(0, [new(board.Id, card.Id, Date)]))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(Url + "/focus", new FocusWorkspacePlanDto(1, board.Id, card.Id))).EnsureSuccessStatusCode();
        var before = (await client.GetFromJsonAsync<WorkspacePlanDto>(Url))!;
        var response = await client.PostAsJsonAsync(Url + "/focus", new FocusWorkspacePlanDto(
            before.Revision, emptyBoard ? Guid.Empty : board.Id, emptyBoard ? card.Id : Guid.Empty));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("ValidationError");
        (await client.GetFromJsonAsync<WorkspacePlanDto>(Url)).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task ViewerPlanIsPrivateAndForeignCardsCannotBeIntroduced()
    {
        using var owner = factory.CreateClient();
        using var viewer = factory.CreateClient();
        using var other = factory.CreateClient();
        var (board, card) = await Setup(owner);
        var user = await ApiTestHarness.AuthenticateAsync(viewer, "plan-viewer");
        var (foreignBoard, foreignCard) = await Setup(other);
        (await owner.PostAsJsonAsync($"/api/boards/{board.Id}/access", new GrantAccessDto(board.Id, user.UserId, UserRole.Viewer))).EnsureSuccessStatusCode();
        (await viewer.PutAsJsonAsync(Url, new SaveWorkspacePlanDto(0, [new(board.Id, card.Id, Date)]))).EnsureSuccessStatusCode();
        (await viewer.PostAsJsonAsync(Url + "/focus", new FocusWorkspacePlanDto(1, board.Id, card.Id))).EnsureSuccessStatusCode();
        (await owner.GetFromJsonAsync<WorkspacePlanDto>(Url))!.Entries.Should().BeEmpty();
        (await other.GetFromJsonAsync<WorkspacePlanDto>(Url))!.LastWorked.Should().BeNull();
        foreach (var entry in new[] { new PersonalPlanEntry(foreignBoard.Id, foreignCard.Id, Date), new PersonalPlanEntry(board.Id, foreignCard.Id, Date) })
        {
            (await viewer.PutAsJsonAsync(Url, new SaveWorkspacePlanDto(2, [entry]))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await viewer.PostAsJsonAsync(Url + "/focus", new FocusWorkspacePlanDto(2, entry.BoardId, entry.CardId))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        (await viewer.GetFromJsonAsync<WorkspacePlanDto>(Url))!.Revision.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArchivedOrDeletedCardsLoseMetadataButRemainRemovable(bool delete)
    {
        using var client = factory.CreateClient();
        var (board, card) = await Setup(client);
        (await client.PutAsJsonAsync(Url, new SaveWorkspacePlanDto(0, [new(board.Id, card.Id, Date)]))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(Url + "/focus", new FocusWorkspacePlanDto(1, board.Id, card.Id))).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            if (delete) db.Cards.Remove((await db.Cards.FindAsync(card.Id))!);
            else (await db.Boards.FindAsync(board.Id))!.Archive();
            await db.SaveChangesAsync();
        }
        var read = (await client.GetFromJsonAsync<WorkspacePlanDto>(Url))!;
        read.Entries.Single().Available.Should().BeFalse();
        read.Entries.Single().Title.Should().BeNull();
        read.LastWorked!.Title.Should().BeNull();
        (await client.PostAsJsonAsync(Url + "/focus", new FocusWorkspacePlanDto(2, board.Id, card.Id))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync(Url, new SaveWorkspacePlanDto(2, [new(board.Id, card.Id, Date.AddDays(1))]))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync(Url, new SaveWorkspacePlanDto(2, []))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task StaleAndConcurrentWritersCannotOverwriteSavedChoices()
    {
        using var client = factory.CreateClient();
        var (board, card) = await Setup(client);
        (await client.GetAsync(Url)).EnsureSuccessStatusCode();
        using var scope1 = factory.Services.CreateScope();
        using var scope2 = factory.Services.CreateScope();
        var db = scope1.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var userId = (await db.Boards.FindAsync(board.Id))!.OwnerId!.Value;
        var repo1 = scope1.ServiceProvider.GetRequiredService<IWorkspacePlanRepository>();
        var repo2 = scope2.ServiceProvider.GetRequiredService<IWorkspacePlanRepository>();
        var first = await repo1.GetAsync(userId, default);
        var second = await repo2.GetAsync(userId, default);
        first.ReplacePersonalPlan(new([new(board.Id, card.Id, Date)], null));
        second.ReplacePersonalPlan(new([], new(board.Id, card.Id, DateTimeOffset.UtcNow)));
        (await repo1.SaveAsync(first, 0, default)).Should().BeTrue();
        (await repo2.SaveAsync(second, 0, default)).Should().BeFalse();
        (await client.PutAsJsonAsync(Url, new SaveWorkspacePlanDto(0, []))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync(Url + "/focus", new FocusWorkspacePlanDto(0, board.Id, card.Id))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var read = (await client.GetFromJsonAsync<WorkspacePlanDto>(Url))!;
        read.Entries.Should().HaveCount(1);
        read.LastWorked.Should().BeNull();
    }

    [Fact]
    public async Task BothAccountExportsIncludeThePrivatePlan()
    {
        using var client = factory.CreateClient();
        var (board, card) = await Setup(client);
        (await client.PutAsJsonAsync(Url, new SaveWorkspacePlanDto(0, [new(board.Id, card.Id, Date)]))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(Url + "/focus", new FocusWorkspacePlanDto(1, board.Id, card.Id))).EnsureSuccessStatusCode();
        JsonNode? first = null;
        foreach (var path in new[] { "/api/account/export", "/api/account/export/stream" })
        {
            var json = JsonNode.Parse(await client.GetStringAsync(path))!;
            var prefs = json["data"]!["preferences"]!;
            prefs["personalPlanRevision"]!.GetValue<long>().Should().Be(2);
            prefs["personalPlan"]!["entries"]![0]!["cardId"]!.GetValue<Guid>().Should().Be(card.Id);
            prefs["personalPlan"]!["lastWorked"]!["cardId"]!.GetValue<Guid>().Should().Be(card.Id);
            if (first is null) first = prefs.DeepClone();
            else JsonNode.DeepEquals(first, prefs).Should().BeTrue();
        }
        var shared = await client.GetStringAsync($"/api/export/boards/{board.Id}/json");
        shared.Should().NotContain("personalPlan");
    }

    [Fact]
    public async Task RevokedAccessHidesMetadataAndAccountDeletionErasesOnlyOwnPlan()
    {
        using var owner = factory.CreateClient();
        using var viewer = factory.CreateClient();
        var (board, card) = await Setup(owner);
        var user = await ApiTestHarness.AuthenticateAsync(viewer, "plan-erasure");
        (await owner.PostAsJsonAsync($"/api/boards/{board.Id}/access", new GrantAccessDto(board.Id, user.UserId, UserRole.Viewer))).EnsureSuccessStatusCode();
        foreach (var client in new[] { owner, viewer })
            (await client.PutAsJsonAsync(Url, new SaveWorkspacePlanDto(0, [new(board.Id, card.Id, Date)]))).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            await db.Set<BoardAccess>().Where(x => x.UserId == user.UserId && x.BoardId == board.Id).ExecuteDeleteAsync();
        }
        var revoked = (await viewer.GetFromJsonAsync<WorkspacePlanDto>(Url))!;
        revoked.Entries.Single().Available.Should().BeFalse();
        revoked.Entries.Single().Title.Should().BeNull();
        (await viewer.PostAsJsonAsync("/api/account/delete", new AccountDeletionRequest("password123", "DELETE MY ACCOUNT"))).EnsureSuccessStatusCode();
        using var verification = factory.Services.CreateScope();
        var verifyDb = verification.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await verifyDb.Set<UserPreference>().CountAsync(x => x.UserId == user.UserId)).Should().Be(0);
        (await owner.GetFromJsonAsync<WorkspacePlanDto>(Url))!.Entries.Should().HaveCount(1);
        (await verifyDb.Cards.FindAsync(card.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task InvalidAndDuplicateMaterialDoesNotCommit()
    {
        using var client = factory.CreateClient();
        var (board, card) = await Setup(client);
        PersonalPlanEntry entry = new(board.Id, card.Id, Date);
        foreach (var entries in new PersonalPlanEntry[][] { [entry, entry], [entry with { CardId = Guid.Empty }], [entry with { PlannedDate = default }], Enumerable.Range(0, 41).Select(_ => entry with { CardId = Guid.NewGuid() }).ToArray() })
            (await client.PutAsJsonAsync(Url, new SaveWorkspacePlanDto(0, entries))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<WorkspacePlanDto>(Url))!.Revision.Should().Be(0);
    }

    private async Task<(BoardDto, CardDto)> Setup(HttpClient client)
    {
        await ApiTestHarness.AuthenticateAsync(client, "personal-plan");
        var board = await ApiTestHarness.CreateBoardAsync(client, "Plan project");
        var response = await client.PostAsJsonAsync($"/api/boards/{board.Id}/columns", new CreateColumnDto(board.Id, "Next", null, null));
        var column = (await response.Content.ReadFromJsonAsync<ColumnDto>())!;
        var created = await client.PostAsJsonAsync($"/api/boards/{board.Id}/cards", new CreateCardDto(board.Id, column.Id, "Plan a useful next step", null, new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero), null));
        created.EnsureSuccessStatusCode();
        return (board, (await created.Content.ReadFromJsonAsync<CardDto>())!);
    }
}
