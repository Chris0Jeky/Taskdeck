using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class WorkspaceAttentionApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private async Task<(HttpClient Client, Guid User, Guid Board, Guid Card)> Setup()
    {
        var client = factory.CreateClient(); var auth = await ApiTestHarness.AuthenticateAsync(client, "attention");
        var board = await ApiTestHarness.CreateBoardAsync(client);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var column = new Column(board.Id, "Next", 0); var card = new Card(board.Id, column.Id, "Saved question");
        card.Block("A concrete dependency"); db.Columns.Add(column); db.Cards.Add(card); await db.SaveChangesAsync();
        return (client, auth.UserId, board.Id, card.Id);
    }
    private static async Task<WorkspaceAttentionDto> Enable(HttpClient client)
    {
        var settings = (await client.GetFromJsonAsync<WorkspaceAttentionDto>("/api/workspace-attention"))!;
        var response = await client.PutAsJsonAsync("/api/workspace-attention", new SaveWorkspaceAttentionDto(settings.Revision, true));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<WorkspaceAttentionDto>())!;
    }
    [Fact]
    public async Task ExplicitOptInOnlyRemindsAboutExistingAvailableQuestionsAndKeepsExports()
    {
        var (client, user, board, card) = await Setup();
        (await client.GetFromJsonAsync<WorkspaceAttentionDto>("/api/workspace-attention"))!.Enabled.Should().BeFalse();
        var endpoint = "/api/workspace-attention/claim"; var claim = new ClaimWorkspaceAttentionDto(board);
        (await client.PostAsJsonAsync(endpoint, claim)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var enabled = await Enable(client);
        (await client.PostAsJsonAsync(endpoint, claim)).StatusCode.Should().Be(HttpStatusCode.NoContent, "reminders never analyze implicitly");
        (await client.PostAsJsonAsync("/api/workspace-insights/analyze", new AnalyzeWorkspaceDto(board))).EnsureSuccessStatusCode();
        var reminder = await client.PostAsJsonAsync(endpoint, claim); reminder.StatusCode.Should().Be(HttpStatusCode.OK);
        var receipt = (await reminder.Content.ReadFromJsonAsync<WorkspaceAttentionReminderDto>())!;
        receipt.BoardId.Should().Be(board); receipt.InsightId.Should().NotBeEmpty();
        (await client.PostAsJsonAsync(endpoint, claim)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PutAsJsonAsync("/api/workspace-attention", new SaveWorkspaceAttentionDto(enabled.Revision, false)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "a claim advances the account revision");
        foreach (var route in new[] { "/api/account/export", "/api/account/export/stream" })
        {
            var preferences = (await client.GetFromJsonAsync<UserDataExportDto>(route))!.Data.Preferences!;
            preferences.Attention!.Enabled.Should().BeTrue(); preferences.Attention.Count.Should().Be(1);
        }
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Cards.FindAsync(card))!.IsBlocked.Should().BeTrue();
        (await db.Set<QuietInsight>().CountAsync(x => x.UserId == user)).Should().Be(1);
    }
    [Fact]
    public async Task StaleDismissedAndForeignQuestionsCannotConsumeOrProduceAttention()
    {
        var (client, user, board, card) = await Setup(); await Enable(client);
        var analyzed = await client.PostAsJsonAsync("/api/workspace-insights/analyze", new AnalyzeWorkspaceDto(board));
        var insight = (await analyzed.Content.ReadFromJsonAsync<List<QuietInsightDto>>())!.Single();
        (await client.PatchAsJsonAsync($"/api/workspace-insights/{insight.Id}", new InsightActionDto("dismiss"))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/workspace-attention/claim", new ClaimWorkspaceAttentionDto(board))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var stranger = factory.CreateClient(); await ApiTestHarness.AuthenticateAsync(stranger, "attention-stranger"); await Enable(stranger);
        (await stranger.PostAsJsonAsync("/api/workspace-attention/claim", new ClaimWorkspaceAttentionDto(board))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PatchAsJsonAsync($"/api/workspace-insights/{insight.Id}", new InsightActionDto("reopen"))).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>(); (await db.Cards.FindAsync(card))!.Unblock(); await db.SaveChangesAsync();
        }
        (await client.PostAsJsonAsync("/api/workspace-attention/claim", new ClaimWorkspaceAttentionDto(board))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var check = factory.Services.CreateScope();
        (await check.ServiceProvider.GetRequiredService<IWorkspaceAttentionRepository>().GetAsync(user, default)).ReadAttention().Count.Should().Be(0);
        (await factory.CreateClient().PostAsJsonAsync("/api/workspace-attention/claim", new ClaimWorkspaceAttentionDto(board))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
    [Fact]
    public async Task ConcurrentClientsShareOneConditionalBudgetUpdate()
    {
        var (client, user, _, _) = await Setup(); var enabled = await Enable(client);
        using var first = factory.Services.CreateScope(); using var second = factory.Services.CreateScope();
        var one = first.ServiceProvider.GetRequiredService<IWorkspaceAttentionRepository>();
        var two = second.ServiceProvider.GetRequiredService<IWorkspaceAttentionRepository>();
        var state = (await one.GetAsync(user, default)).ReadAttention();
        var now = DateTimeOffset.UtcNow;
        var results = await Task.WhenAll(one.SaveAsync(user, enabled.Revision, state.Claim(Guid.NewGuid(), now)!, default),
            two.SaveAsync(user, enabled.Revision, state.Claim(Guid.NewGuid(), now)!, default));
        results.Count(x => x).Should().Be(1);
        using var fresh = factory.Services.CreateScope();
        (await fresh.ServiceProvider.GetRequiredService<IWorkspaceAttentionRepository>().GetAsync(user, default)).ReadAttention().Count.Should().Be(1);
    }
}
