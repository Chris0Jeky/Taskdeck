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
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed partial class ChatContextApiTests
{
    [Fact]
    public async Task OriginalSelectionSendsOnlyChosenHistoricalTextAndRetainsExactReceipt()
    {
        var requests = new List<ChatCompletionRequest>(); using var factory = CreateFactory(requests);
        using var client = factory.CreateClient(); var (actor, board, _, memory, session) = await Setup(factory, client);
        Guid originalId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var saved = await db.Set<WorkspaceMemory>().FindAsync(memory.Id);
            var intake = new CaptureIntakeService(scope.ServiceProvider.GetRequiredService<ICaptureStore>(), null);
            await intake.StageMemorySourcesAsync(saved!); await db.SaveChangesAsync(); originalId = saved!.AnswerSourceAssetId!.Value;
            saved.Revise(saved.Title, "Corrected answer should not be included", "needsReview");
            await intake.StageMemorySourcesAsync(saved); await db.SaveChangesAsync();
        }
        var pageResponse = await client.GetAsync(SourceUrl(board.Id, memory.Id, 2)); pageResponse.EnsureSuccessStatusCode();
        pageResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
        var page = (await pageResponse.Content.ReadFromJsonAsync<ChatAssetPage>())!;
        page.Items.Should().HaveCount(2);
        var original = page.Items.Single(asset => asset.Id == originalId);
        original.SupersededByAssetId.Should().NotBeNull(); original.Excerpt.Should().Be(memory.Text);
        var selection = new ChatContextSelection(null, false, [], [new(memory.Id, 2, original.Id, original.ContentHash)]);
        (await client.PostAsJsonAsync(Url(session.Id), new SendChatMessageDto("What did I originally say?", Context: selection))).EnsureSuccessStatusCode();
        requests.Should().ContainSingle();
        requests[0].BoardContext.Should().Contain(memory.Text).And.Contain("superseded historical source").And.NotContain("Corrected answer should not be included");
        var savedSession = (await client.GetFromJsonAsync<ChatSessionDto>($"/api/llm/chat/sessions/{session.Id}"))!;
        var receipt = savedSession.RecentMessages.Single(message => message.Role == ChatMessageRole.User).ContextSources!.Single();
        receipt.Id.Should().Be(originalId); receipt.MemoryId.Should().Be(memory.Id); receipt.ContentHash.Should().Be(original.ContentHash);
        receipt.SupersededByAssetId.Should().Be(original.SupersededByAssetId);
        requests.Clear();
        using var replay = factory.Services.CreateScope();
        await foreach (var item in replay.ServiceProvider.GetRequiredService<IChatService>().StreamResponseAsync(session.Id, actor)) item.Error.Should().BeNull();
        requests.Should().ContainSingle().Which.BoardContext.Should().Contain(memory.Text);
    }

    [Theory]
    [InlineData("hash", HttpStatusCode.Conflict)]
    [InlineData("revision", HttpStatusCode.Conflict)]
    [InlineData("asset", HttpStatusCode.Forbidden)]
    [InlineData("memory", HttpStatusCode.Forbidden)]
    [InlineData("archived", HttpStatusCode.Forbidden)]
    [InlineData("duplicate", HttpStatusCode.BadRequest)]
    [InlineData("budget", HttpStatusCode.BadRequest)]
    [InlineData("bad-hash", HttpStatusCode.BadRequest)]
    public async Task InvalidOriginalSelectionNeverDispatchesOrWritesTurn(string mutation, HttpStatusCode expected)
    {
        var requests = new List<ChatCompletionRequest>(); using var factory = CreateFactory(requests);
        using var client = factory.CreateClient(); var (_, board, _, memory, session) = await Setup(factory, client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>(); var saved = await db.Set<WorkspaceMemory>().FindAsync(memory.Id);
            await new CaptureIntakeService(scope.ServiceProvider.GetRequiredService<ICaptureStore>(), null).StageMemorySourcesAsync(saved!); await db.SaveChangesAsync();
        }
        var page = (await client.GetFromJsonAsync<ChatAssetPage>(SourceUrl(board.Id, memory.Id, 1)))!;
        var asset = page.Items.Single();
        var reference = new ChatAssetReference(mutation == "memory" ? Guid.NewGuid() : memory.Id, mutation == "revision" ? 2 : 1,
            mutation == "asset" ? Guid.NewGuid() : asset.Id, mutation == "hash" ? new string('0', 64) : mutation == "bad-hash" ? "bad" : asset.ContentHash);
        if (mutation == "archived")
        {
            using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Set<WorkspaceMemory>().FindAsync(memory.Id))!.SetArchived(true); await db.SaveChangesAsync();
        }
        var selection = new ChatContextSelection(null, false,
            mutation == "budget" ? Enumerable.Range(0, 5).Select(_ => new ChatMemoryReference(Guid.NewGuid(), 1)).ToArray() : [],
            mutation == "duplicate" ? [reference, reference] : [reference]);
        var response = await client.PostAsJsonAsync(Url(session.Id), new SendChatMessageDto("Consider my original", Context: selection));
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        requests.Should().BeEmpty();
        (await client.GetFromJsonAsync<ChatSessionDto>($"/api/llm/chat/sessions/{session.Id}"))!.RecentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task OriginalPagesAreBoundedPrivateVersionedAndReadWithoutHistoryTracking()
    {
        var requests = new List<ChatCompletionRequest>(); using var factory = CreateFactory(requests);
        using var client = factory.CreateClient(); using var anonymous = factory.CreateClient(); using var other = factory.CreateClient();
        var (actor, board, _, memory, _) = await Setup(factory, client);
        await ApiTestHarness.AuthenticateAsync(other, "original-context-other");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>(); var saved = await db.Set<WorkspaceMemory>().FindAsync(memory.Id);
            var intake = new CaptureIntakeService(scope.ServiceProvider.GetRequiredService<ICaptureStore>(), null);
            await intake.StageMemorySourcesAsync(saved!); await db.SaveChangesAsync();
            for (var index = 0; index < 11; index++)
            {
                saved!.Revise(saved.Title, new string('x', 1600) + index, "unknown");
                await intake.StageMemorySourcesAsync(saved); await db.SaveChangesAsync();
            }
        }
        var url = SourceUrl(board.Id, memory.Id, 12);
        (await anonymous.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await other.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync(SourceUrl(Guid.NewGuid(), memory.Id, 12))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync(SourceUrl(board.Id, memory.Id, 1))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetAsync(url + "&offset=-1")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var page = (await client.GetFromJsonAsync<ChatAssetPage>(url))!;
        page.Items.Should().HaveCount(10); page.NextOffset.Should().Be(10);
        page.Items.Skip(1).Should().OnlyContain(asset => asset.Truncated && asset.Excerpt.Length == 1500);
        var lastPage = (await client.GetFromJsonAsync<ChatAssetPage>(url + "&offset=10"))!;
        lastPage.Items.Should().HaveCount(2); lastPage.NextOffset.Should().BeNull();
        using var read = factory.Services.CreateScope(); var context = read.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await read.ServiceProvider.GetRequiredService<IChatSourceReader>().MemoryAsync(actor, memory.Id, default))!.Revision.Should().Be(12);
        context.ChangeTracker.Entries<WorkspaceMemory>().Should().BeEmpty();
        context.ChangeTracker.Entries<WorkspaceMemoryRevision>().Should().BeEmpty();
        requests.Should().BeEmpty();
    }
    [Fact]
    public async Task ExistingAssetFromAnotherMemoryCannotBeSubstitutedAndReplayRejectsCorrection()
    {
        var requests = new List<ChatCompletionRequest>(); using var factory = CreateFactory(requests);
        using var client = factory.CreateClient(); var (actor, board, _, memory, session) = await Setup(factory, client);
        Guid foreignId; string foreignHash;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>(); var saved = await db.Set<WorkspaceMemory>().FindAsync(memory.Id);
            var second = new WorkspaceMemory(actor, board.Id, "Unselected", "Never disclose this original", "statement"); db.Add(second);
            var intake = new CaptureIntakeService(scope.ServiceProvider.GetRequiredService<ICaptureStore>(), null);
            await intake.StageMemorySourcesAsync(saved!); await intake.StageMemorySourcesAsync(second); await db.SaveChangesAsync();
            foreignId = second.AnswerSourceAssetId!.Value;
            foreignHash = (await db.Set<SourceAsset>().FindAsync(foreignId))!.ContentHash;
        }
        var substituted = new ChatContextSelection(null, false, [], [new(memory.Id, 1, foreignId, foreignHash)]);
        (await client.PostAsJsonAsync(Url(session.Id), new SendChatMessageDto("Consider this original", Context: substituted))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        requests.Should().BeEmpty();
        var page = (await client.GetFromJsonAsync<ChatAssetPage>(SourceUrl(board.Id, memory.Id, 1)))!;
        var original = page.Items.Single();
        var valid = new ChatContextSelection(null, false, [], [new(memory.Id, 1, original.Id, original.ContentHash)]);
        (await client.PostAsJsonAsync(Url(session.Id), new SendChatMessageDto("Consider this original", Context: valid))).EnsureSuccessStatusCode();
        requests.Single().BoardContext.Should().NotContain("Never disclose this original"); requests.Clear();
        (await client.PutAsJsonAsync($"/api/workspace-memory/{memory.Id}", new UpdateWorkspaceMemoryDto(memory.Title, "Corrected source", "statement", 1))).EnsureSuccessStatusCode();
        using var replay = factory.Services.CreateScope(); var events = new List<LlmTokenEvent>();
        await foreach (var item in replay.ServiceProvider.GetRequiredService<IChatService>().StreamResponseAsync(session.Id, actor)) events.Add(item);
        events.Should().ContainSingle().Which.Error.Should().Contain("changed"); requests.Should().BeEmpty();
    }

    [Fact]
    public async Task OriginalPagesContinuePastOneThousandWithoutLoadingFullHistory()
    {
        var requests = new List<ChatCompletionRequest>(); using var factory = CreateFactory(requests);
        using var client = factory.CreateClient(); var (actor, board, _, memory, _) = await Setup(factory, client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var saved = (await db.Set<WorkspaceMemory>().FindAsync(memory.Id))!;
            var store = scope.ServiceProvider.GetRequiredService<ICaptureStore>();
            await new CaptureIntakeService(store, null).StageMemorySourcesAsync(saved); await db.SaveChangesAsync();
            var capture = (await store.GetByIdForUserAsync(saved.SourceCaptureId!.Value, actor))!;
            for (var index = 1; index <= 1011; index++)
            {
                var replacement = capture.SupersedeInlineTextSource($"Historical correction {index}");
                db.Add(replacement);
            }
            await db.SaveChangesAsync();
        }
        var url = SourceUrl(board.Id, memory.Id, 1);
        var boundary = (await client.GetFromJsonAsync<ChatAssetPage>(url + "&offset=1000"))!;
        boundary.Items.Should().HaveCount(10); boundary.NextOffset.Should().BeNull(); boundary.NextAfterOrdinal.Should().Be(1009);
        var first = (await client.GetFromJsonAsync<ChatAssetPage>(url + "&afterOrdinal=999"))!;
        first.Items.Should().HaveCount(10); first.NextAfterOrdinal.Should().Be(1009); first.NextOffset.Should().BeNull();
        var final = (await client.GetFromJsonAsync<ChatAssetPage>(url + $"&afterOrdinal={first.NextAfterOrdinal}"))!;
        final.Items.Should().HaveCount(2); final.NextAfterOrdinal.Should().BeNull();
        final.Items.Last().Excerpt.Should().Be("Historical correction 1011");
        first.Items.Select(x => x.Id).Intersect(final.Items.Select(x => x.Id)).Should().BeEmpty();
        (await client.GetAsync(url + "&afterOrdinal=2147483647")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(url + "&offset=2147483647")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync(url + "&afterOrdinal=-2")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync(url + "&afterOrdinal=1&offset=1")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SourceCursorIncludesOrdinalZeroAndSkipsNonTextGapsWithoutLosingHistory()
    {
        var requests = new List<ChatCompletionRequest>(); using var factory = CreateFactory(requests);
        using var client = factory.CreateClient(); var (actor, board, _, memory, _) = await Setup(factory, client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var saved = (await db.Set<WorkspaceMemory>().FindAsync(memory.Id))!;
            var store = scope.ServiceProvider.GetRequiredService<ICaptureStore>();
            await new CaptureIntakeService(store, null).StageMemorySourcesAsync(saved); await db.SaveChangesAsync();
            var capture = (await store.GetByIdForUserAsync(saved.SourceCaptureId!.Value, actor))!;
            for (var index = 0; index < 11; index++)
            {
                db.Add(capture.AddExternalReferenceSource($"https://example.invalid/source-{index}"));
                db.Add(capture.SupersedeInlineTextSource($"Correction after gap {index}"));
            }
            await db.SaveChangesAsync();
        }
        var url = SourceUrl(board.Id, memory.Id, 1);
        var first = (await client.GetFromJsonAsync<ChatAssetPage>(url + "&afterOrdinal=-1"))!;
        first.Items.Select(x => x.Ordinal).Should().Equal(Enumerable.Range(0, 10).Select(x => x * 2));
        first.NextAfterOrdinal.Should().Be(18);
        var next = (await client.GetFromJsonAsync<ChatAssetPage>(url + $"&afterOrdinal={first.NextAfterOrdinal}"))!;
        next.Items.Select(x => x.Ordinal).Should().Equal(20, 22); next.NextAfterOrdinal.Should().BeNull();
        first.Items.Select(x => x.Id).Intersect(next.Items.Select(x => x.Id)).Should().BeEmpty();
        next.Items.Last().Excerpt.Should().Be("Correction after gap 10");
        requests.Should().BeEmpty();
    }

    private static string SourceUrl(Guid board, Guid memory, int revision) => $"/api/llm/chat/context-memory/{memory}/sources?boardId={board}&revision={revision}";
}
