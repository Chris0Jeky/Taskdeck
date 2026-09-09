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
using Taskdeck.Infrastructure.Repositories;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ThinkingAnswerApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private async Task<(HttpClient Owner, Guid Board, Guid Card, ThinkingLayer Question)> Setup()
    {
        var owner = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(owner, "question-owner");
        var board = await ApiTestHarness.CreateBoardAsync(owner);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var column = new Column(board.Id, "Thinking", 0);
        var card = new Card(board.Id, column.Id, "Shared task");
        db.Columns.Add(column); db.Cards.Add(card);
        var question = new ThinkingLayer(Guid.NewGuid(), "question", "What remains unknown?", "Shared question context", []);
        var deck = new ThinkingDeck(card.Id); deck.Replace([question]); db.Add(deck);
        await db.SaveChangesAsync();
        return (owner, board.Id, card.Id, question);
    }
    private static string Url(Guid board, Guid card, Guid layer) => $"/api/boards/{board}/cards/{card}/thinking/questions/{layer}/answer";

    [Fact]
    public async Task PrivateAnswerSupportsViewerAndDeduplicatesWithoutLeakingToCollaboratorsOrExports()
    {
        var (owner, board, card, question) = await Setup();
        using var viewer = factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(viewer, "question-viewer");
        (await owner.PostAsJsonAsync($"/api/boards/{board}/access", new GrantAccessDto(board, user.UserId, UserRole.Viewer))).EnsureSuccessStatusCode();
        var url = Url(board, card, question.Id);
        var request = new ThinkingAnswerDto(1, "Private answer only for this person", "unknown");
        var response = await viewer.PostAsJsonAsync(url, request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var memory = (await response.Content.ReadFromJsonAsync<WorkspaceMemoryDto>())!;
        memory.ThinkingSource.Should().Be(new ThinkingAnswerSourceDto(card, question.Id, 1));
        memory.OriginalEvidence.Should().Contain("Shared question context");
        var repeated = await viewer.PostAsJsonAsync(url, request);
        (await repeated.Content.ReadFromJsonAsync<WorkspaceMemoryDto>())!.Id.Should().Be(memory.Id);
        (await viewer.GetFromJsonAsync<List<WorkspaceMemoryDto>>($"/api/workspace-memory?boardId={board}")).Should().HaveCount(1);
        (await owner.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.GetFromJsonAsync<List<WorkspaceMemoryDto>>($"/api/workspace-memory?boardId={board}"))!.Should().BeEmpty();
        (await owner.PutAsJsonAsync($"/api/workspace-memory/{memory.Id}", new UpdateWorkspaceMemoryDto("Hijack", "No", "statement", 1))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var export = await owner.GetStringAsync($"/api/export/boards/{board}/json");
        export.Should().NotContain(request.Text).And.NotContain(memory.Id.ToString());
        var shared = await viewer.GetStringAsync($"/api/boards/{board}/cards/{card}/thinking");
        shared.Should().NotContain(request.Text).And.NotContain(memory.Id.ToString());
        var insights = await viewer.PostAsJsonAsync("/api/workspace-insights/analyze", new AnalyzeWorkspaceDto(board));
        (await insights.Content.ReadFromJsonAsync<List<QuietInsightDto>>())!.Should().Contain(x => x.MemoryId == memory.Id && x.Rule == "memory-review");
        (await viewer.PutAsJsonAsync($"/api/workspace-memory/{memory.Id}", new UpdateWorkspaceMemoryDto(memory.Title, "Corrected private answer", "statement", memory.Revision))).EnsureSuccessStatusCode();
        var corrected = (await viewer.GetFromJsonAsync<WorkspaceMemoryDto>(url))!;
        corrected.History.Should().Contain(x => x.Text == request.Text);
        corrected.OriginalEvidence.Should().Be(memory.OriginalEvidence);
        (await viewer.PostAsJsonAsync(url, request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AuthMembershipSavedKindAndRevisionAreRequired()
    {
        var (owner, board, card, question) = await Setup();
        using var outsider = factory.CreateClient();
        var url = Url(board, card, question.Id);
        var dto = new ThinkingAnswerDto(1, "My answer", "statement");
        await ApiTestHarness.AssertUnauthorizedAsync(await outsider.PostAsJsonAsync(url, dto));
        await ApiTestHarness.AuthenticateAsync(outsider, "question-outsider");
        (await outsider.PostAsJsonAsync(url, dto)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await outsider.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PostAsJsonAsync(url, dto with { ExpectedRevision = 0 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await owner.PostAsJsonAsync(Url(board, card, Guid.NewGuid()), dto)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var another = await ApiTestHarness.CreateBoardAsync(owner);
        (await owner.PostAsJsonAsync(Url(another.Id, card, question.Id), dto)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PutAsJsonAsync($"/api/boards/{board}/cards/{card}/thinking", new SaveThinkingDeckDto(1, [question with { Kind = "note" }]))).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync(url, dto with { ExpectedRevision = 2 })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RemovingQuestionPreservesPrivateEvidenceAndMemory()
    {
        var (owner, board, card, question) = await Setup();
        var response = await owner.PostAsJsonAsync(Url(board, card, question.Id), new ThinkingAnswerDto(1, "Keep this evidence", "statement"));
        var memory = (await response.Content.ReadFromJsonAsync<WorkspaceMemoryDto>())!;
        (await owner.PutAsJsonAsync($"/api/boards/{board}/cards/{card}/thinking", new SaveThinkingDeckDto(1, []))).EnsureSuccessStatusCode();
        var kept = (await owner.GetFromJsonAsync<List<WorkspaceMemoryDto>>($"/api/workspace-memory?boardId={board}"))!.Single();
        kept.Id.Should().Be(memory.Id); kept.OriginalEvidence.Should().Be(memory.OriginalEvidence);
        (await owner.GetAsync(Url(board, card, question.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SourceRevisionGuardRollsBackPrivateAnswerWhenQuestionChangedDuringSave()
    {
        var (_, board, card, question) = await Setup();
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var firstDecks = new ThinkingDeckRepository(firstDb);
        var secondDecks = new ThinkingDeckRepository(secondDb);
        var stale = (await firstDecks.GetAsync(card, default))!;
        var current = (await secondDecks.GetAsync(card, default))!;
        current.Replace([question with { Body = "New source" }]);
        (await secondDecks.SaveAsync(current, 1, default)).Should().BeTrue();
        var ownerId = (await firstDb.Boards.FindAsync(board))!.OwnerId!.Value;
        var memory = new WorkspaceMemory(ownerId, board, "Stale source", "Should roll back", "statement");
        var repository = new WorkspaceInsightRepository(firstDb);
        repository.Add(memory); firstDecks.GuardRevision(stale);
        (await repository.SaveAsync(default)).Should().BeFalse();
        (await secondDb.Set<WorkspaceMemory>().AnyAsync(x => x.Id == memory.Id)).Should().BeFalse();
    }
}
