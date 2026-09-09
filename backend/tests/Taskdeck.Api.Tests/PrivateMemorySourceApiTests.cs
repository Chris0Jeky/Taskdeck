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

public sealed class PrivateMemorySourceApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task OriginalAndCorrectionsSurviveBoardDeletionInBothPrivateExportsAndAccountErasureDeletesThem()
    {
        using var client = factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "source-owner");
        var board = await ApiTestHarness.CreateBoardAsync(client);
        var create = await client.PostAsJsonAsync("/api/workspace-memory", new CreateWorkspaceMemoryDto(board.Id, "Original", "First answer", "unknown"));
        create.EnsureSuccessStatusCode();
        var original = (await create.Content.ReadFromJsonAsync<WorkspaceMemoryDto>())!;
        var edit = await client.PutAsJsonAsync($"/api/workspace-memory/{original.Id}", new UpdateWorkspaceMemoryDto("Corrected", "Second answer", "statement", 1));
        edit.EnsureSuccessStatusCode();
        var memory = (await edit.Content.ReadFromJsonAsync<WorkspaceMemoryDto>())!;
        (await client.PutAsJsonAsync($"/api/workspace-memory/{original.Id}", new UpdateWorkspaceMemoryDto("Stale", "Must not enter sources", "statement", 1))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PatchAsJsonAsync($"/api/workspace-memory/{memory.Id}", new ArchiveWorkspaceMemoryDto(true, memory.Revision))).EnsureSuccessStatusCode();
        var source = (await client.GetFromJsonAsync<UserDataExportNativeCaptureDto>($"/api/workspace-memory/{memory.Id}/sources"))!;
        (await client.GetAsync($"/api/workspace-memory/{memory.Id}/sources")).Headers.CacheControl!.NoStore.Should().BeTrue();
        source.Capture.SourceAssets.Should().HaveCount(2);
        using var other = factory.CreateClient();
        (await other.GetAsync($"/api/workspace-memory/{memory.Id}/sources")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await ApiTestHarness.AuthenticateAsync(other, "source-other");
        foreach (var route in new[] { "/api/account/export", "/api/account/export/stream" })
        {
            var export = (await client.GetFromJsonAsync<UserDataExportDto>(route))!;
            export.Data.NativeCaptures!.Single(x => x.Id == source.Id).Should().BeEquivalentTo(source);
            export.Data.WorkspaceMemories!.Single(x => x.Id == memory.Id).AnswerSourceAssetId.Should().Be(memory.Sources!.AnswerAssetId);
            (await other.GetFromJsonAsync<UserDataExportDto>(route))!.Data.NativeCaptures.Should().BeEmpty();
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.LlmRequests.AnyAsync(x => x.UserId == user.UserId)).Should().BeFalse("saving memory must not enqueue processing");
            // The native source has an independent retention lifetime; board deletion cascades memory only.
            await db.Boards.Where(x => x.Id == board.Id).ExecuteDeleteAsync();
        }
        foreach (var route in new[] { "/api/account/export", "/api/account/export/stream" })
        {
            var export = (await client.GetFromJsonAsync<UserDataExportDto>(route))!;
            export.Data.WorkspaceMemories.Should().BeEmpty();
            export.Data.NativeCaptures!.Single().BoardId.Should().BeNull();
            export.Data.NativeCaptures!.Single().Capture.SourceAssets.Select(x => x.Text).Should().BeEquivalentTo("First answer", "Second answer");
        }
        (await client.PostAsJsonAsync("/api/account/delete", new AccountDeletionRequest("password123", "DELETE MY ACCOUNT"))).EnsureSuccessStatusCode();
        using var deletedScope = factory.Services.CreateScope();
        var deletedDb = deletedScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await deletedDb.Captures.AnyAsync(x => x.UserId == user.UserId)).Should().BeFalse();
        (await deletedDb.SourceAssets.AnyAsync(x => x.CaptureId == source.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task ExistingHistoryIsAdmittedOnCorrectionWithoutReplacingSavedOriginals()
    {
        using var client = factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "source-history");
        var board = await ApiTestHarness.CreateBoardAsync(client);
        var memory = new WorkspaceMemory(user.UserId, board.Id, "Saved question", "Original", "unknown", originalEvidence: "The original question");
        memory.Revise("Saved question", "Earlier correction", "assumption");
        memory.SetArchived(true); memory.SetArchived(false);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            db.Add(memory); await db.SaveChangesAsync();
        }
        var response = await client.PutAsJsonAsync($"/api/workspace-memory/{memory.Id}", new UpdateWorkspaceMemoryDto(memory.Title, "Latest correction", "statement", memory.Revision));
        response.EnsureSuccessStatusCode();
        var updated = (await response.Content.ReadFromJsonAsync<WorkspaceMemoryDto>())!;
        var sources = (await client.GetFromJsonAsync<UserDataExportNativeCaptureDto>($"/api/workspace-memory/{memory.Id}/sources"))!.Capture.SourceAssets;
        sources.Should().HaveCount(4, "archive and status changes reuse the same immutable text");
        updated.History.Should().OnlyContain(x => x.AnswerSourceAssetId != null);
        updated.OriginalText.Should().Be("Original");
        foreach (var revision in updated.History)
            sources.Single(x => x.Id == revision.AnswerSourceAssetId).Text.Should().Be(revision.Text);
        sources.Single(x => x.Id == updated.Sources!.EvidenceAssetId).Text.Should().Be("The original question");
    }

    [Fact]
    public async Task CompetingCorrectionRollsBackSourceSupersessionWithTheLosingMemoryRevision()
    {
        using var client = factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "source-race");
        var board = await ApiTestHarness.CreateBoardAsync(client);
        var create = await client.PostAsJsonAsync("/api/workspace-memory", new CreateWorkspaceMemoryDto(board.Id, "Title", "Original", "unknown"));
        create.EnsureSuccessStatusCode();
        var original = (await create.Content.ReadFromJsonAsync<WorkspaceMemoryDto>())!;
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();
        var firstDb = first.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var firstRepo = new WorkspaceInsightRepository(firstDb);
        var secondRepo = new WorkspaceInsightRepository(secondDb);
        var stale = (await firstRepo.MemoryAsync(user.UserId, original.Id, default))!;
        var winner = (await secondRepo.MemoryAsync(user.UserId, original.Id, default))!;
        stale.Revise("Title", "Losing correction", "statement");
        await new CaptureIntakeService(first.ServiceProvider.GetRequiredService<ICaptureStore>(), null).StageMemorySourcesAsync(stale);
        winner.Revise("Title", "Winning correction", "statement");
        await new CaptureIntakeService(second.ServiceProvider.GetRequiredService<ICaptureStore>(), null).StageMemorySourcesAsync(winner);
        (await secondRepo.SaveAsync(default)).Should().BeTrue();
        (await firstRepo.SaveAsync(default)).Should().BeFalse();
        var sources = (await client.GetFromJsonAsync<UserDataExportNativeCaptureDto>($"/api/workspace-memory/{original.Id}/sources"))!.Capture.SourceAssets;
        sources.Select(x => x.Text).Should().BeEquivalentTo("Original", "Winning correction");
        sources.Single(x => x.Text == "Original").SupersededByAssetId.Should().Be(winner.AnswerSourceAssetId);
    }
}
