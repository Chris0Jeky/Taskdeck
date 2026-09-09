using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class WorkspaceInsightApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private async Task<(HttpClient Client, Guid BoardId, Guid CardId)> Setup()
    {
        var client = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "quiet");
        var board = await ApiTestHarness.CreateBoardAsync(client);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var column = new Column(board.Id, "Working", 0);
        var card = new Card(board.Id, column.Id, "Release rehearsal");
        card.Block("Waiting for a test device");
        db.Columns.Add(column); db.Cards.Add(card); await db.SaveChangesAsync();
        return (client, board.Id, card.Id);
    }
    private static async Task<List<QuietInsightDto>> Analyze(HttpClient client, Guid boardId)
    {
        var response = await client.PostAsJsonAsync("/api/workspace-insights/analyze", new AnalyzeWorkspaceDto(boardId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<QuietInsightDto>>())!;
    }
    [Fact]
    public async Task Analysis_DeduplicatesAndDismissalSurvivesRecheck()
    {
        var (client, board, _) = await Setup();
        var first = (await Analyze(client, board)).Single();
        var again = (await Analyze(client, board)).Single();
        again.Id.Should().Be(first.Id);
        (await client.PatchAsJsonAsync($"/api/workspace-insights/{first.Id}", new InsightActionDto("dismiss"))).EnsureSuccessStatusCode();
        (await Analyze(client, board)).Single().State.Should().Be("dismissed");
    }
    [Fact]
    public async Task Answer_RetainsOriginalWithoutChangingCard_AndCorrectionKeepsHistory()
    {
        var (client, board, cardId) = await Setup();
        var insight = (await Analyze(client, board)).Single();
        const string answer = "  Borrow a device from the lab.\nAsk first.  ";
        var response = await client.PostAsJsonAsync($"/api/workspace-insights/{insight.Id}/answer", new AnswerInsightDto(answer, "statement", insight.Evidence));
        response.EnsureSuccessStatusCode();
        var memory = (await response.Content.ReadFromJsonAsync<WorkspaceMemoryDto>())!;
        memory.OriginalText.Should().Be(answer);
        (await Analyze(client, board)).Single().State.Should().Be("resolved");
        using (var scope = factory.Services.CreateScope())
        {
            var card = await scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>().Cards.FindAsync(cardId);
            card!.IsBlocked.Should().BeTrue(); card.BlockReason.Should().Be("Waiting for a test device");
        }
        var edit = await client.PutAsJsonAsync($"/api/workspace-memory/{memory.Id}", new UpdateWorkspaceMemoryDto(memory.Title, "Use the test pool.", "assumption", memory.Revision));
        edit.EnsureSuccessStatusCode();
        var changed = (await edit.Content.ReadFromJsonAsync<WorkspaceMemoryDto>())!;
        changed.History.Single().Text.Should().Be(answer); changed.OriginalText.Should().Be(answer);
        var stale = await client.PutAsJsonAsync($"/api/workspace-memory/{memory.Id}", new UpdateWorkspaceMemoryDto(memory.Title, "Stale edit", "statement", memory.Revision));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
    [Fact]
    public async Task AnsweredQuestion_ReopensWhenItsEvidenceChanges_WithoutLosingOriginalBasis()
    {
        var (client, board, cardId) = await Setup();
        var insight = (await Analyze(client, board)).Single();
        var response = await client.PostAsJsonAsync($"/api/workspace-insights/{insight.Id}/answer", new AnswerInsightDto("Borrow a device", "statement", insight.Evidence));
        response.EnsureSuccessStatusCode();
        var memory = (await response.Content.ReadFromJsonAsync<WorkspaceMemoryDto>())!;
        memory.OriginalEvidence.Should().Be(insight.Evidence);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Cards.FindAsync(cardId))!.Block("Device available, waiting for permission"); await db.SaveChangesAsync();
        }
        (await Analyze(client, board)).Single().State.Should().Be("available");
        (await Analyze(client, board)).Single().State.Should().Be("available");
    }
    [Fact]
    public async Task Archive_ExcludesWorkingMemory_RestoreRetainsHistory()
    {
        var (client, board, _) = await Setup();
        var create = await client.PostAsJsonAsync("/api/workspace-memory", new CreateWorkspaceMemoryDto(board, "Audience", "Audience unknown", "unknown"));
        create.EnsureSuccessStatusCode();
        var memory = (await create.Content.ReadFromJsonAsync<WorkspaceMemoryDto>())!;
        (await Analyze(client, board)).Should().Contain(x => x.MemoryId == memory.Id);
        var archive = await client.PatchAsJsonAsync($"/api/workspace-memory/{memory.Id}", new ArchiveWorkspaceMemoryDto(true, memory.Revision));
        archive.EnsureSuccessStatusCode();
        var archived = (await archive.Content.ReadFromJsonAsync<WorkspaceMemoryDto>())!;
        (await client.GetFromJsonAsync<List<WorkspaceMemoryDto>>($"/api/workspace-memory?boardId={board}")).Should().BeEmpty();
        (await Analyze(client, board)).Single(x => x.MemoryId == memory.Id).State.Should().Be("resolved");
        var restore = await client.PatchAsJsonAsync($"/api/workspace-memory/{memory.Id}", new ArchiveWorkspaceMemoryDto(false, archived.Revision));
        restore.EnsureSuccessStatusCode();
        (await restore.Content.ReadFromJsonAsync<WorkspaceMemoryDto>())!.History.Should().HaveCount(2);
    }
    [Fact]
    public async Task CauseDisappears_SnoozedQuestionResolves_ReopenDoesNotReviveIt()
    {
        var (client, board, cardId) = await Setup();
        var insight = (await Analyze(client, board)).Single();
        (await client.PatchAsJsonAsync($"/api/workspace-insights/{insight.Id}", new InsightActionDto("snooze"))).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Cards.FindAsync(cardId))!.Unblock(); await db.SaveChangesAsync();
        }
        var response = await client.PatchAsJsonAsync($"/api/workspace-insights/{insight.Id}", new InsightActionDto("reopen"));
        response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync<QuietInsightDto>())!.State.Should().Be("resolved");
    }
    [Fact]
    public async Task Mute_SuppressesNewQuestionsInSameBoardFamily()
    {
        var (client, board, _) = await Setup();
        var insight = (await Analyze(client, board)).Single();
        (await client.PatchAsJsonAsync($"/api/workspace-insights/{insight.Id}", new InsightActionDto("mute"))).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var column = new Column(board, "Another", 1); var card = new Card(board, column.Id, "Another blocked card"); card.Block("A prerequisite");
            db.Columns.Add(column); db.Cards.Add(card); await db.SaveChangesAsync();
        }
        (await Analyze(client, board)).Should().HaveCount(2).And.OnlyContain(x => x.State == "muted");
    }
    [Fact]
    public async Task UnauthenticatedAndCrossUserRequests_DoNotDiscloseRecords()
    {
        var (client, board, _) = await Setup();
        var insight = (await Analyze(client, board)).Single();
        var memoryResponse = await client.PostAsJsonAsync("/api/workspace-memory", new CreateWorkspaceMemoryDto(board, "Private", "Private evidence", "statement"));
        var memory = (await memoryResponse.Content.ReadFromJsonAsync<WorkspaceMemoryDto>())!;
        using var other = factory.CreateClient();
        (await other.GetAsync($"/api/workspace-insights?boardId={board}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await other.PostAsJsonAsync("/api/workspace-memory", new CreateWorkspaceMemoryDto(board,"x","y","statement"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await ApiTestHarness.AuthenticateAsync(other, "quiet-other");
        (await other.PostAsJsonAsync("/api/workspace-insights/analyze", new AnalyzeWorkspaceDto(board))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PatchAsJsonAsync($"/api/workspace-insights/{insight.Id}", new InsightActionDto("dismiss"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsJsonAsync($"/api/workspace-insights/{insight.Id}/answer", new AnswerInsightDto("x", "statement", insight.Evidence))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync($"/api/workspace-memory/{memory.Id}", new UpdateWorkspaceMemoryDto("x","y","statement",1))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PatchAsJsonAsync($"/api/workspace-memory/{memory.Id}", new ArchiveWorkspaceMemoryDto(true,1))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
    [Fact]
    public async Task ChangedEvidence_RejectsAnswerFromOldQuestion()
    {
        var (client, board, cardId) = await Setup();
        var insight = (await Analyze(client, board)).Single();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Cards.FindAsync(cardId))!.Block("A different prerequisite"); await db.SaveChangesAsync();
        }
        var response = await client.PostAsJsonAsync($"/api/workspace-insights/{insight.Id}/answer", new AnswerInsightDto("Old answer", "statement", insight.Evidence));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetFromJsonAsync<List<WorkspaceMemoryDto>>($"/api/workspace-memory?boardId={board}")).Should().BeEmpty();
    }
    [Fact]
    public async Task BoardCollaborator_CannotReadOrEditAnotherPersonsMemories()
    {
        var (client, board, _) = await Setup();
        var create = await client.PostAsJsonAsync("/api/workspace-memory", new CreateWorkspaceMemoryDto(board, "Private", "Personal thought", "needsReview"));
        var memory = (await create.Content.ReadFromJsonAsync<WorkspaceMemoryDto>())!;
        var ownerInsight = (await Analyze(client, board)).Single(x => x.MemoryId == memory.Id);
        using var reader = factory.CreateClient();
        var person = await ApiTestHarness.AuthenticateAsync(reader, "quiet-reader");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            var owner = (await db.Boards.FindAsync(board))!.OwnerId!.Value;
            db.BoardAccesses.Add(new BoardAccess(board, person.UserId, Taskdeck.Domain.Enums.UserRole.Viewer, owner));
            await db.SaveChangesAsync();
        }
        (await reader.GetFromJsonAsync<List<WorkspaceMemoryDto>>($"/api/workspace-memory?boardId={board}")).Should().BeEmpty();
        (await Analyze(reader, board)).Should().NotContain(x => x.MemoryId == memory.Id);
        (await reader.PutAsJsonAsync($"/api/workspace-memory/{memory.Id}", new UpdateWorkspaceMemoryDto("Changed","Changed","statement",memory.Revision))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await reader.PostAsJsonAsync($"/api/workspace-insights/{ownerInsight.Id}/answer", new AnswerInsightDto("Changed","statement",ownerInsight.Evidence))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
    [Fact]
    public async Task ArchivedBoard_DeniesMemoryAndInsightAccess()
    {
        var (client, board, _) = await Setup();
        await Analyze(client, board);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Boards.FindAsync(board))!.Archive(); await db.SaveChangesAsync();
        }
        (await client.GetAsync($"/api/workspace-insights?boardId={board}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/workspace-memory?boardId={board}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
