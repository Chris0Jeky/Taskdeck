using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

public class WorkspaceObservationApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private sealed class LateReadRace { public Func<Task>? Change; }
    private sealed class LateReader(WorkspaceObservationReader inner, LateReadRace race) : IWorkspaceObservationReader
    {
        private int reads;
        public async Task<ObservationSourceDto?> SourceAsync(Guid userId, Guid boardId, Guid cardId, CancellationToken ct)
        {
            var source = await inner.SourceAsync(userId, boardId, cardId, ct);
            if (++reads == 3 && race.Change != null) await race.Change();
            return source;
        }
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("archive")]
    [InlineData("delete")]
    [InlineData("revoke")]
    public async Task ChangeAfterFinalServiceRead_IsRejectedAtCommit(string change)
    {
        var provider = new Provider(); var race = new LateReadRace();
        using var configured = WithProvider(provider);
        using var app = configured.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWorkspaceObservationReader>();
            services.AddScoped<IWorkspaceObservationReader>(sp => new LateReader(new WorkspaceObservationReader(sp.GetRequiredService<TaskdeckDbContext>()), race));
        }));
        var (client, board, cardId, source) = await Setup(app);
        Guid? viewerId = null;
        if (change == "revoke")
        {
            client = app.CreateClient(); viewerId = (await ApiTestHarness.AuthenticateAsync(client, "late-viewer")).UserId;
            using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var owner = await db.Boards.Where(x => x.Id == board).Select(x => x.OwnerId).SingleAsync();
            db.BoardAccesses.Add(new BoardAccess(board, viewerId.Value, UserRole.Viewer, owner!.Value));
            await db.SaveChangesAsync();
        }
        race.Change = async () =>
        {
            using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            if (change == "archive") await db.Boards.Where(x => x.Id == board).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsArchived, true));
            else if (change == "delete") await db.Cards.Where(x => x.Id == cardId).ExecuteDeleteAsync();
            else if (change == "revoke") await db.BoardAccesses.Where(x => x.BoardId == board && x.UserId == viewerId).ExecuteDeleteAsync();
            else await db.Cards.Where(x => x.Id == cardId).ExecuteUpdateAsync(set => set.SetProperty(x => x.Title, "Later committed evidence"));
        };
        (await Generate(client, board, source)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        provider.Calls.Should().Be(1);
        using var verify = app.Services.CreateScope();
        (await verify.ServiceProvider.GetRequiredService<TaskdeckDbContext>().Set<QuietInsight>().CountAsync(x => x.BoardId == board)).Should().Be(0);
    }

    [Fact]
    public async Task RejectedCommitDetachesStagedQuestionsFromLaterSaves()
    {
        using var app = WithProvider(new Provider()); var (_, board, card, source) = await Setup(app);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var user = (await db.Boards.FindAsync(board))!.OwnerId!.Value;
        var repository = scope.ServiceProvider.GetRequiredService<IWorkspaceInsightRepository>();
        repository.Add(new QuietInsight(user, board, "model:next-step", card.ToString(), card, null));
        (await repository.SaveObservationAsync(user, board, card, "changed-fingerprint", default)).Should().BeFalse();
        await db.SaveChangesAsync();
        (await db.Set<QuietInsight>().CountAsync(x => x.BoardId == board)).Should().Be(0);
    }

    private sealed class Provider : ILlmProvider
    {
        public int Calls;
        public ChatCompletionRequest? Request;
        public Func<Task>? DuringCall;
        public string Output = """[{"kind":"next-step","question":"What would make the next step clear?","reason":"The card does not identify its next action.","quote":"Investigate rollout"}]""";
        public bool IsMock;
        public async Task<LlmCompletionResult> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
        {
            Calls++; Request = request;
            if (DuringCall != null) await DuringCall();
            return new(Output, 90, false, Provider: "ObservationFixture", Model: "fixture-v1");
        }
        public Task<LlmHealthStatus> GetHealthAsync(CancellationToken ct = default) => Task.FromResult(new LlmHealthStatus(true, "ObservationFixture", IsMock: IsMock));
        public Task<LlmHealthStatus> ProbeAsync(CancellationToken ct = default) => GetHealthAsync(ct);
        public IAsyncEnumerable<LlmTokenEvent> StreamAsync(ChatCompletionRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private WebApplicationFactory<Program> WithProvider(Provider provider) => factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.RemoveAll<ILlmProvider>(); services.AddScoped<ILlmProvider>(_ => provider);
    }));
    private static async Task<(HttpClient Client, Guid Board, Guid Card, ObservationSourceDto Source)> Setup(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient(); await ApiTestHarness.AuthenticateAsync(client, "observe");
        var board = await ApiTestHarness.CreateBoardAsync(client);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var column = new Column(board.Id, "Working", 0);
        var card = new Card(board.Id, column.Id, "Investigate rollout");
        db.Columns.Add(column); db.Cards.Add(card); await db.SaveChangesAsync();
        var source = await client.GetFromJsonAsync<ObservationSourceDto>($"/api/workspace-insights/observation-source?boardId={board.Id}&cardId={card.Id}");
        return (client, board.Id, card.Id, source!);
    }
    private static Task<HttpResponseMessage> Generate(HttpClient client, Guid board, ObservationSourceDto source) =>
        client.PostAsJsonAsync("/api/workspace-insights/model-analysis", new GenerateObservationsDto(board, source.CardId, source.Fingerprint));

    [Fact]
    public async Task QuotedQuestions_ArePrivateDeduplicatedAndDoNotChangeBoard()
    {
        var provider = new Provider(); using var app = WithProvider(provider);
        var (client, board, cardId, source) = await Setup(app);
        var first = await Generate(client, board, source); first.EnsureSuccessStatusCode();
        var insight = (await first.Content.ReadFromJsonAsync<List<QuietInsightDto>>())!.Single();
        var again = await Generate(client, board, source); again.EnsureSuccessStatusCode();
        (await again.Content.ReadFromJsonAsync<List<QuietInsightDto>>())!.Single().Id.Should().Be(insight.Id);
        provider.Request!.Messages.Should().ContainSingle();
        provider.Request.Messages[0].Content.Should().Contain(source.Fingerprint);
        (await client.GetFromJsonAsync<List<QuietInsightDto>>($"/api/workspace-insights?boardId={board}"))!.Single().State.Should().Be("available");
        (await client.PatchAsJsonAsync($"/api/workspace-insights/{insight.Id}", new InsightActionDto("dismiss"))).EnsureSuccessStatusCode();
        var dismissed = await Generate(client, board, source); dismissed.EnsureSuccessStatusCode();
        (await dismissed.Content.ReadFromJsonAsync<List<QuietInsightDto>>())!.Should().BeEmpty();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Cards.FindAsync(cardId))!.Title.Should().Be(source.Title);
        (await db.Set<QuietInsight>().CountAsync(x => x.BoardId == board)).Should().Be(1);
        var outsider = app.CreateClient(); await ApiTestHarness.AuthenticateAsync(outsider, "outsider");
        (await Generate(outsider, board, source)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        provider.Calls.Should().Be(3);
    }
    [Theory]
    [InlineData("edit")]
    [InlineData("archive")]
    [InlineData("delete")]
    public async Task SourceChangeDuringModelCall_DiscardsAllCandidates(string change)
    {
        var provider = new Provider(); using var app = WithProvider(provider);
        var (client, board, cardId, source) = await Setup(app);
        provider.DuringCall = async () =>
        {
            using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            if (change == "archive") await db.Boards.Where(x => x.Id == board).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsArchived, true));
            else if (change == "delete") await db.Cards.Where(x => x.Id == cardId).ExecuteDeleteAsync();
            else await db.Cards.Where(x => x.Id == cardId).ExecuteUpdateAsync(set => set.SetProperty(x => x.Title, "Updated evidence"));
        };
        (await Generate(client, board, source)).StatusCode.Should().Be(change == "edit" ? HttpStatusCode.Conflict : HttpStatusCode.NotFound);
        using var scope = app.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>().Set<QuietInsight>().CountAsync(x => x.BoardId == board)).Should().Be(0);
    }
    [Fact]
    public async Task MembershipRevokedDuringModelCall_ReturnsNoPrivateCandidate()
    {
        var provider = new Provider(); using var app = WithProvider(provider); var (_, board, card, _) = await Setup(app);
        var collaborator = app.CreateClient(); var identity = await ApiTestHarness.AuthenticateAsync(collaborator,"observer");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var owner = await db.Boards.Where(x => x.Id == board).Select(x => x.OwnerId).SingleAsync();
            db.BoardAccesses.Add(new BoardAccess(board,identity.UserId,UserRole.Viewer,owner!.Value)); await db.SaveChangesAsync();
        }
        var source = (await collaborator.GetFromJsonAsync<ObservationSourceDto>($"/api/workspace-insights/observation-source?boardId={board}&cardId={card}"))!;
        provider.DuringCall = async () =>
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>().BoardAccesses.Where(x => x.BoardId == board && x.UserId == identity.UserId).ExecuteDeleteAsync();
        };
        (await Generate(collaborator,board,source)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var check = app.Services.CreateScope();
        (await check.ServiceProvider.GetRequiredService<TaskdeckDbContext>().Set<QuietInsight>().CountAsync(x => x.BoardId == board)).Should().Be(0);
    }
    [Fact]
    public async Task AnsweredCategory_StaysClosedAcrossParaphrasedModelRuns()
    {
        var provider = new Provider(); using var app = WithProvider(provider); var (client, board, _, source) = await Setup(app);
        var response = await Generate(client, board, source); response.EnsureSuccessStatusCode();
        var insight = (await response.Content.ReadFromJsonAsync<List<QuietInsightDto>>())!.Single();
        (await client.PostAsJsonAsync($"/api/workspace-insights/{insight.Id}/answer",new AnswerInsightDto("Ask the release owner for the rollout checklist.","statement",insight.Evidence))).EnsureSuccessStatusCode();
        provider.Output = provider.Output.Replace("What would make the next step clear?", "What is your next action?");
        var again = await Generate(client,board,source); again.EnsureSuccessStatusCode();
        (await again.Content.ReadFromJsonAsync<List<QuietInsightDto>>())!.Should().BeEmpty();
        (await client.GetFromJsonAsync<List<QuietInsightDto>>($"/api/workspace-insights?boardId={board}"))!.Single().State.Should().Be("resolved");
    }
    [Fact]
    public async Task InvalidQuoteAndMockProvider_SaveNothing()
    {
        var provider = new Provider { Output = """[{"kind":"outcome","question":"What is due Friday?","reason":"Invented deadline","quote":"Friday"}]""" };
        using var app = WithProvider(provider); var (client, board, _, source) = await Setup(app);
        (await Generate(client, board, source)).IsSuccessStatusCode.Should().BeFalse();
        provider.IsMock = true;
        (await Generate(client, board, source)).IsSuccessStatusCode.Should().BeFalse();
        provider.Calls.Should().Be(1);
        (await client.GetFromJsonAsync<List<QuietInsightDto>>($"/api/workspace-insights?boardId={board}"))!.Should().BeEmpty();
    }
    [Fact]
    public async Task ExpiredOrEditedEvidence_CannotBeAnsweredOrReopened()
    {
        var provider = new Provider(); using var app = WithProvider(provider); var (client, board, _, source) = await Setup(app);
        var response = await Generate(client, board, source); response.EnsureSuccessStatusCode();
        var insight = (await response.Content.ReadFromJsonAsync<List<QuietInsightDto>>())!.Single();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var evidence = JsonSerializer.Deserialize<ObservationEvidence>(insight.Evidence)! with { GeneratedAt = DateTimeOffset.UtcNow.AddDays(-2) };
            var serialized = JsonSerializer.Serialize(evidence);
            await db.Set<QuietInsight>().Where(x => x.Id == insight.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.Evidence, serialized));
        }
        (await client.GetFromJsonAsync<List<QuietInsightDto>>($"/api/workspace-insights?boardId={board}"))!.Single().State.Should().Be("resolved");
        (await client.PostAsJsonAsync($"/api/workspace-insights/{insight.Id}/answer", new AnswerInsightDto("Stale answer", "statement", insight.Evidence))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PatchAsJsonAsync($"/api/workspace-insights/{insight.Id}", new InsightActionDto("reopen"))).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<List<QuietInsightDto>>($"/api/workspace-insights?boardId={board}"))!.Single().State.Should().Be("resolved");
    }
}
