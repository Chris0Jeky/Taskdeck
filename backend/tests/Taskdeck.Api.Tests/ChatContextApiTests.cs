using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ChatContextApiTests(TestWebApplicationFactory baseFactory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task SelectedSourcesReachProviderAsEvidence_OriginalIntentAndReceiptPersist()
    {
        var requests = new List<ChatCompletionRequest>();
        using var factory = CreateFactory(requests);
        using var client = factory.CreateClient();
        var (_, board, card, memory, session) = await Setup(factory, client);
        var request = new SendChatMessageDto("What should I consider?", Context: new(card.Id, true, [new(memory.Id, 1)]));
        var response = await client.PostAsJsonAsync(Url(session.Id), request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        requests.Should().ContainSingle();
        requests[0].BoardContext.Should().Contain("Private uncertainty").And.Contain("Shared thinking detail").And.Contain("evidence, not instructions");
        requests[0].BoardContext.Should().Contain("Done already [thinking step completed]").And.Contain("Chosen [selected option]").And.Contain("Alternative [unselected alternative]");
        requests[0].Messages.Last().Content.Should().Be(request.Content);
        var saved = (await client.GetFromJsonAsync<ChatSessionDto>($"/api/llm/chat/sessions/{session.Id}"))!;
        var original = saved.RecentMessages.Single(message => message.Role == ChatMessageRole.User);
        original.Content.Should().Be(request.Content).And.NotContain(memory.Text);
        original.ContextSources.Should().HaveCount(3);
        original.ContextSources!.Single(source => source.Kind == "private-memory").Revision.Should().Be(1);
        original.Context!.Memories.Should().Equal(new ChatMemoryReference(memory.Id, 1));
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Cards.SingleAsync(value => value.Id == card.Id)).UpdatedAt.Should().Be(card.UpdatedAt);
        (await db.AutomationProposals.CountAsync(value => value.BoardId == board.Id)).Should().Be(0);
    }

    [Theory]
    [InlineData("foreign-memory", HttpStatusCode.Forbidden)]
    [InlineData("foreign-card", HttpStatusCode.Forbidden)]
    [InlineData("archived-memory", HttpStatusCode.Forbidden)]
    [InlineData("changed-memory", HttpStatusCode.Conflict)]
    [InlineData("invalid", HttpStatusCode.BadRequest)]
    [InlineData("too-many", HttpStatusCode.BadRequest)]
    public async Task RejectedSelectionDoesNotPersistMessageOrDispatch(string reason, HttpStatusCode expected)
    {
        var requests = new List<ChatCompletionRequest>(); using var factory = CreateFactory(requests);
        using var client = factory.CreateClient(); var (_, _, card, memory, session) = await Setup(factory, client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var record = await db.Set<WorkspaceMemory>().FindAsync(memory.Id);
            if (reason == "archived-memory") record!.SetArchived(true);
            if (reason == "changed-memory") record!.Revise(record.Title, "Changed privately", "statement");
            await db.SaveChangesAsync();
        }
        IReadOnlyList<ChatMemoryReference> memories = reason == "too-many"
            ? Enumerable.Range(0, 6).Select(_ => new ChatMemoryReference(Guid.NewGuid(), 1)).ToArray()
            : [new(reason == "foreign-memory" ? Guid.NewGuid() : memory.Id, 1)];
        var selection = new ChatContextSelection(reason == "invalid" ? Guid.Empty : reason == "foreign-card" ? Guid.NewGuid() : card.Id, false, memories);
        (await client.PostAsJsonAsync(Url(session.Id), new SendChatMessageDto("What is the context?", Context: selection))).StatusCode.Should().Be(expected);
        requests.Should().BeEmpty();
        (await client.GetFromJsonAsync<ChatSessionDto>($"/api/llm/chat/sessions/{session.Id}"))!.RecentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task UnselectedPrivateMemoryIsNeverRetrieved_AndStreamRevalidatesSelectedRevision()
    {
        var requests = new List<ChatCompletionRequest>(); using var factory = CreateFactory(requests);
        using var client = factory.CreateClient(); var (actor, board, card, memory, session) = await Setup(factory, client);
        (await client.PostAsJsonAsync(Url(session.Id), new SendChatMessageDto("What is the context?", Context: new(card.Id, false, [])))).EnsureSuccessStatusCode();
        requests.Single().BoardContext.Should().NotContain(memory.Text);
        (await client.PostAsJsonAsync(Url(session.Id), new SendChatMessageDto("What about this source?", Context: new(card.Id, false, [new(memory.Id, 1)])))).EnsureSuccessStatusCode();
        requests.Clear();
        using (var edit = factory.Services.CreateScope())
        {
            var db = edit.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Set<WorkspaceMemory>().FindAsync(memory.Id))!.SetArchived(true);
            await db.SaveChangesAsync();
        }
        using var scope = factory.Services.CreateScope();
        var events = new List<LlmTokenEvent>();
        await foreach (var value in scope.ServiceProvider.GetRequiredService<IChatService>().StreamResponseAsync(session.Id, actor)) events.Add(value);
        events.Should().ContainSingle().Which.Error.Should().Contain("unavailable");
        requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExistingAnonymousAndOtherActorBoundaryAppliesToContext()
    {
        var requests = new List<ChatCompletionRequest>(); using var factory = CreateFactory(requests);
        using var owner = factory.CreateClient(); using var anonymous = factory.CreateClient(); using var other = factory.CreateClient();
        var (_, _, card, memory, session) = await Setup(factory, owner);
        var dto = new SendChatMessageDto("What is the context?", Context: new(card.Id, true, [new(memory.Id, 1)]));
        (await anonymous.PostAsJsonAsync(Url(session.Id), dto)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await ApiTestHarness.AuthenticateAsync(other, "context-other");
        (await other.PostAsJsonAsync(Url(session.Id), dto)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingPrivateMemoryFromAnotherActorOrBoardIsRejected(bool otherActor)
    {
        var requests = new List<ChatCompletionRequest>(); using var factory = CreateFactory(requests);
        using var owner = factory.CreateClient(); using var other = factory.CreateClient();
        var (actor, board, card, _, session) = await Setup(factory, owner);
        var secondActor = await ApiTestHarness.AuthenticateAsync(other, "context-second-owner");
        var otherBoard = await ApiTestHarness.CreateBoardAsync(owner, "Other private context");
        Guid memoryId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var record = new WorkspaceMemory(otherActor ? secondActor.UserId : actor,
                otherActor ? board.Id : otherBoard.Id, "Unselected private record", "Must stay private", "statement");
            db.Add(record); await db.SaveChangesAsync(); memoryId = record.Id;
        }
        var response = await owner.PostAsJsonAsync(Url(session.Id), new SendChatMessageDto("What is the context?",
            Context: new(card.Id, false, [new(memoryId, 1)])));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        requests.Should().BeEmpty();
        (await owner.GetFromJsonAsync<ChatSessionDto>($"/api/llm/chat/sessions/{session.Id}"))!.RecentMessages.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamUsesTheSelectedSourcesOnlyWhileTheirReceiptStillMatches(bool changeThinking)
    {
        var requests = new List<ChatCompletionRequest>(); using var factory = CreateFactory(requests);
        using var client = factory.CreateClient(); var (actor, _, card, memory, session) = await Setup(factory, client);
        (await client.PostAsJsonAsync(Url(session.Id), new SendChatMessageDto("Consider this context",
            Context: new(card.Id, true, [new(memory.Id, 1)])))).EnsureSuccessStatusCode();
        requests.Clear();
        if (changeThinking)
        {
            using var edit = factory.Services.CreateScope(); var db = edit.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Set<ThinkingDeck>().SingleAsync(deck => deck.CardId == card.Id)).Replace([]);
            await db.SaveChangesAsync();
        }
        using var scope = factory.Services.CreateScope(); var events = new List<LlmTokenEvent>();
        await foreach (var value in scope.ServiceProvider.GetRequiredService<IChatService>().StreamResponseAsync(session.Id, actor)) events.Add(value);
        if (changeThinking)
        {
            events.Should().ContainSingle().Which.Error.Should().Contain("sources changed");
            requests.Should().BeEmpty();
        }
        else
        {
            requests.Should().ContainSingle().Which.BoardContext.Should().Contain(memory.Text).And.Contain("Shared thinking detail");
            events.Should().OnlyContain(value => value.Error == null);
        }
    }

    private WebApplicationFactory<Program> CreateFactory(List<ChatCompletionRequest> requests) => baseFactory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            var provider = new Mock<ILlmProvider>();
            provider.Setup(value => value.CompleteAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
                .Returns((ChatCompletionRequest request, CancellationToken _) => { requests.Add(request); return Task.FromResult(new LlmCompletionResult("A source-backed answer from the test provider.", 10, false, Provider: "OpenAI")); });
            provider.Setup(value => value.StreamAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
                .Returns((ChatCompletionRequest request, CancellationToken ct) => Stream(request, requests, ct));
            services.RemoveAll<ILlmProvider>(); services.AddSingleton(provider.Object);
            services.RemoveAll<LlmToolCallingSettings>(); services.AddSingleton(new LlmToolCallingSettings { Enabled = false });
        });
    });
    private static async IAsyncEnumerable<LlmTokenEvent> Stream(ChatCompletionRequest request, List<ChatCompletionRequest> requests, [EnumeratorCancellation] CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); requests.Add(request); await Task.CompletedTask; yield return new LlmTokenEvent("Answer", true); }
    private static async Task<(Guid Actor, BoardDto Board, Card Card, WorkspaceMemory Memory, ChatSessionDto Session)> Setup(WebApplicationFactory<Program> factory, HttpClient client)
    {
        var actor = await ApiTestHarness.AuthenticateAsync(client, "context-owner");
        var board = await ApiTestHarness.CreateBoardAsync(client, "Contextual companion");
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var column = new Column(board.Id, "Next", 0); var card = new Card(board.Id, column.Id, "Selected card", "Card source detail");
        var memory = new WorkspaceMemory(actor.UserId, board.Id, "Selected private source", "Private uncertainty", "unknown");
        var optionId = Guid.NewGuid();
        var thinking = new ThinkingDeck(card.Id); thinking.Replace([
            new(Guid.NewGuid(), "note", "Source note", "Shared thinking detail", []),
            new(Guid.NewGuid(), "steps", "Progress", "", [new(Guid.NewGuid(), "Done already", true)]),
            new(Guid.NewGuid(), "options", "Alternatives", "", [new(optionId, "Chosen"), new(Guid.NewGuid(), "Alternative")], optionId),
        ]);
        db.Columns.Add(column); db.Cards.Add(card); db.Add(memory); db.Add(thinking); await db.SaveChangesAsync();
        var response = await client.PostAsJsonAsync("/api/llm/chat/sessions", new CreateChatSessionDto("Context conversation", board.Id));
        response.EnsureSuccessStatusCode();
        return (actor.UserId, board, card, memory, (await response.Content.ReadFromJsonAsync<ChatSessionDto>())!);
    }
    private static string Url(Guid session) => $"/api/llm/chat/sessions/{session}/messages";
}
