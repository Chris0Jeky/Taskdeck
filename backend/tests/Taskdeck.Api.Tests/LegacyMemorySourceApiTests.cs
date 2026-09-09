using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class LegacyMemorySourceApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private const string Route = "/api/workspace-memory/preserve-sources";

    [Fact]
    public async Task BatchPreservesExactHistoryIncludingArchivedMemoryAndCanBeRetriedWithoutDuplicates()
    {
        using var client = factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "legacy-sources");
        var board = await ApiTestHarness.CreateBoardAsync(client);
        var first = new WorkspaceMemory(user.UserId, board.Id, "Question", "  Original\nanswer  ", "unknown", originalEvidence: "Exact question");
        first.Revise("Question corrected", "Corrected\nanswer", "statement");
        var second = new WorkspaceMemory(user.UserId, board.Id, "Archived", "Saved answer", "assumption");
        second.SetArchived(true);
        await Seed(first, second);
        var response = await client.PostAsJsonAsync(Route, Request(board.Id, first, second));
        response.EnsureSuccessStatusCode();
        var saved = (await response.Content.ReadFromJsonAsync<List<WorkspaceMemoryDto>>())!;
        saved.Should().HaveCount(2);
        foreach (var original in new[] { first, second })
        {
            var memory = saved.Single(x => x.Id == original.Id);
            memory.Revision.Should().Be(original.Revision + 1);
            memory.Text.Should().Be(original.Text);
            memory.OriginalText.Should().Be(original.OriginalText);
            memory.Archived.Should().Be(original.Archived);
            var source = (await client.GetFromJsonAsync<UserDataExportNativeCaptureDto>($"/api/workspace-memory/{memory.Id}/sources"))!;
            foreach (var historical in memory.History)
                source.Capture.SourceAssets.Single(x => x.Id == historical.AnswerSourceAssetId).Text.Should().Be(historical.Text);
        }
        var retry = await client.PostAsJsonAsync(Route, new PreserveMemorySourcesDto(board.Id, saved.Select(x => new PreserveMemorySourceItem(x.Id, x.Revision)).ToList()));
        retry.EnsureSuccessStatusCode();
        (await retry.Content.ReadFromJsonAsync<List<WorkspaceMemoryDto>>()).Should().BeEquivalentTo(saved);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Captures.CountAsync(x => x.UserId == user.UserId)).Should().Be(2);
        (await db.LlmRequests.AnyAsync(x => x.UserId == user.UserId)).Should().BeFalse();
    }

    [Theory]
    [InlineData("stale", HttpStatusCode.Conflict)]
    [InlineData("other-owner", HttpStatusCode.NotFound)]
    [InlineData("other-board", HttpStatusCode.NotFound)]
    [InlineData("archived-board", HttpStatusCode.NotFound)]
    [InlineData("duplicate", HttpStatusCode.BadRequest)]
    [InlineData("oversized", HttpStatusCode.BadRequest)]
    public async Task InvalidBatchAdmitsNothing(string failure, HttpStatusCode expected)
    {
        using var client = factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "legacy-invalid");
        var board = await ApiTestHarness.CreateBoardAsync(client);
        var anotherBoard = await ApiTestHarness.CreateBoardAsync(client);
        using var other = factory.CreateClient();
        var otherUser = await ApiTestHarness.AuthenticateAsync(other, "legacy-other");
        var first = new WorkspaceMemory(user.UserId, board.Id, "First", "Original", "unknown");
        var second = new WorkspaceMemory(failure == "other-owner" ? otherUser.UserId : user.UserId,
            failure == "other-board" ? anotherBoard.Id : board.Id, "Second", "Original", "unknown");
        await Seed(first, second);
        var request = Request(board.Id, first, second);
        if (failure == "stale") request.Memories[1] = new(second.Id, second.Revision + 1);
        if (failure == "duplicate") request.Memories[1] = request.Memories[0];
        if (failure == "oversized") while (request.Memories.Count < 51) request.Memories.Add(new(Guid.NewGuid(), 1));
        if (failure == "archived-board")
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Boards.FindAsync(board.Id))!.Archive(); await db.SaveChangesAsync();
        }
        (await client.PostAsJsonAsync(Route, request)).StatusCode.Should().Be(expected);
        using var verify = factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await verifyDb.Set<WorkspaceMemory>().FindAsync(first.Id))!.SourceCaptureId.Should().BeNull();
        (await verifyDb.Captures.AnyAsync(x => x.UserId == user.UserId)).Should().BeFalse();
    }

    [Fact]
    public async Task MissingIdentityAndBoardMembershipCannotPreserveSources()
    {
        using var client = factory.CreateClient();
        var request = new PreserveMemorySourcesDto(Guid.NewGuid(), [new(Guid.NewGuid(), 1)]);
        (await client.PostAsJsonAsync(Route, request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await ApiTestHarness.AuthenticateAsync(client, "legacy-no-access");
        (await client.PostAsJsonAsync(Route, request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CompetingAdmissionRollsBackTheLosingCapture()
    {
        using var client = factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "legacy-race");
        var board = await ApiTestHarness.CreateBoardAsync(client);
        var original = new WorkspaceMemory(user.UserId, board.Id, "Original", "Exact answer", "unknown");
        await Seed(original);
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();
        var firstRepo = new WorkspaceInsightRepository(first.ServiceProvider.GetRequiredService<TaskdeckDbContext>());
        var secondRepo = new WorkspaceInsightRepository(second.ServiceProvider.GetRequiredService<TaskdeckDbContext>());
        var losing = (await firstRepo.MemoryAsync(user.UserId, original.Id, default))!;
        var winning = (await secondRepo.MemoryAsync(user.UserId, original.Id, default))!;
        losing.BeginSourcePreservation(); winning.BeginSourcePreservation();
        await new CaptureIntakeService(first.ServiceProvider.GetRequiredService<ICaptureStore>(), null).StageMemorySourcesAsync(losing);
        await new CaptureIntakeService(second.ServiceProvider.GetRequiredService<ICaptureStore>(), null).StageMemorySourcesAsync(winning);
        (await secondRepo.SaveAsync(default)).Should().BeTrue();
        (await firstRepo.SaveAsync(default)).Should().BeFalse();
        using var verify = factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Captures.Where(x => x.UserId == user.UserId).Select(x => x.Id).ToListAsync()).Should().Equal(winning.SourceCaptureId!.Value);
    }

    private async Task Seed(params WorkspaceMemory[] memories)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        db.AddRange(memories); await db.SaveChangesAsync();
    }
    private static PreserveMemorySourcesDto Request(Guid board, params WorkspaceMemory[] memories) =>
        new(board, memories.Select(x => new PreserveMemorySourceItem(x.Id, x.Revision)).ToList());
}
