using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class WorkspacePrivacyPortabilityApiTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private sealed record Fixture(HttpClient Client, Guid UserId, Guid OtherId, Guid BoardId, Guid CardId,
        Guid ActiveMemoryId, Guid ArchivedMemoryId, Guid InsightId, Guid LayerId);

    private async Task<Fixture> Seed()
    {
        using var ownerClient = factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "privacy-owner");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "surviving-shared");
        var client = factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "privacy-member");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        db.BoardAccesses.Add(new BoardAccess(board.Id, user.UserId, UserRole.Editor, owner.UserId));
        var column = new Column(board.Id, "Work", 0);
        var card = new Card(board.Id, column.Id, "Shared work stays");
        db.Columns.Add(column); db.Cards.Add(card);
        var layerId = Guid.NewGuid();
        var deck = new ThinkingDeck(card.Id);
        deck.Replace(new[] { new ThinkingLayer(layerId, "question", "Shared question", "Shared working material", Array.Empty<ThinkingItem>()) });
        db.Set<ThinkingDeck>().Add(deck);
        var insight = new QuietInsight(user.UserId, board.Id, "blocked-next-step", card.Id.ToString(), card.Id, null);
        insight.Refresh("Private question", "Private rationale", "PRIVATE-EVIDENCE", DateTimeOffset.UtcNow);
        insight.Act("snooze", DateTimeOffset.UtcNow);
        var active = new WorkspaceMemory(user.UserId, board.Id, "Private active", "  ORIGINAL-A\n  ", "unknown", insight.Id, "PRIVATE-EVIDENCE");
        active.AttachThinkingSource(card.Id, layerId, 1, new string('a', 64));
        active.Revise("Private active corrected", "Corrected active", "statement");
        var archived = new WorkspaceMemory(user.UserId, board.Id, "Private archived", "ORIGINAL-B", "assumption");
        archived.Revise("Private archived corrected", "Corrected archived", "needsReview");
        archived.SetArchived(true);
        db.Set<WorkspaceMemory>().AddRange(active, archived,
            new WorkspaceMemory(owner.UserId, board.Id, "OTHER-USER-PRIVATE", "OTHER-USER-SECRET", "statement"));
        var otherInsight = new QuietInsight(owner.UserId, board.Id, "blocked-next-step", card.Id.ToString(), card.Id, null);
        otherInsight.Refresh("OTHER-USER-QUESTION", "OTHER-USER-DETAIL", "OTHER-USER-EVIDENCE", DateTimeOffset.UtcNow);
        db.Set<QuietInsight>().AddRange(insight, otherInsight);
        await db.SaveChangesAsync();
        return new(client, user.UserId, owner.UserId, board.Id, card.Id, active.Id, archived.Id, insight.Id, layerId);
    }

    [Theory]
    [InlineData("/api/account/export")]
    [InlineData("/api/account/export/stream")]
    public async Task AccountExport_IncludesAllOwnPrivateWorkspaceMaterial(string route)
    {
        var f = await Seed();
        using var client = f.Client;
        // Export is account portability, not the active-board UI projection: even private
        // material on a now-archived board must accompany the user's data.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            (await db.Boards.FindAsync(f.BoardId))!.Archive();
            await db.SaveChangesAsync();
        }
        var response = await client.GetAsync(route);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().EndWith("}", "the streaming response must be complete");
        json.Should().NotContain("OTHER-USER-");
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("data");
        var memories = data.GetProperty("workspaceMemories").EnumerateArray().ToArray();
        memories.Should().HaveCount(2);
        var active = memories.Single(x => x.GetProperty("id").GetGuid() == f.ActiveMemoryId);
        active.GetProperty("originalText").GetString().Should().Be("  ORIGINAL-A\n  ");
        active.GetProperty("originalEvidence").GetString().Should().Be("PRIVATE-EVIDENCE");
        active.GetProperty("insightId").GetGuid().Should().Be(f.InsightId);
        active.GetProperty("sourceCardId").GetGuid().Should().Be(f.CardId);
        active.GetProperty("sourceLayerId").GetGuid().Should().Be(f.LayerId);
        active.GetProperty("sourceDeckRevision").GetInt64().Should().Be(1);
        active.GetProperty("sourceQuestionHash").GetString().Should().Be(new string('a', 64));
        active.GetProperty("history").EnumerateArray().Single().GetProperty("text").GetString().Should().Be("  ORIGINAL-A\n  ");
        active.GetProperty("history")[0].GetProperty("status").GetString().Should().Be("unknown");
        var archived = memories.Single(x => x.GetProperty("id").GetGuid() == f.ArchivedMemoryId);
        archived.GetProperty("archived").GetBoolean().Should().BeTrue();
        archived.GetProperty("revision").GetInt32().Should().Be(3);
        archived.GetProperty("originalText").GetString().Should().Be("ORIGINAL-B");
        archived.GetProperty("history").GetArrayLength().Should().Be(2);
        archived.GetProperty("history")[1].GetProperty("archived").GetBoolean().Should().BeFalse();
        var insight = data.GetProperty("quietInsights").EnumerateArray().Single();
        insight.GetProperty("id").GetGuid().Should().Be(f.InsightId);
        insight.GetProperty("evidence").GetString().Should().Be("PRIVATE-EVIDENCE");
        insight.GetProperty("detail").GetString().Should().Be("Private rationale");
        insight.GetProperty("state").GetString().Should().Be("snoozed");
        insight.GetProperty("snoozeUntil").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task AccountDeletion_ErasesOwnPrivateRowsAndHistory_OnSurvivingSharedBoard()
    {
        var f = await Seed();
        using var client = f.Client;
        var response = await client.PostAsJsonAsync("/api/account/delete", new AccountDeletionRequest("password123", "DELETE MY ACCOUNT"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<AccountDeletionResultDto>())!;
        result.WorkspaceMemoriesDeleted.Should().Be(2);
        result.WorkspaceMemoryRevisionsDeleted.Should().Be(3);
        result.QuietInsightsDeleted.Should().Be(1);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        // Raw relational checks, independent of the UI/service's owner filters.
        (await db.Set<WorkspaceMemory>().CountAsync(x => x.UserId == f.UserId)).Should().Be(0);
        (await db.Set<WorkspaceMemoryRevision>().CountAsync(x => x.MemoryId == f.ActiveMemoryId || x.MemoryId == f.ArchivedMemoryId)).Should().Be(0);
        (await db.Set<QuietInsight>().CountAsync(x => x.UserId == f.UserId)).Should().Be(0);
        (await db.Set<WorkspaceMemory>().CountAsync(x => x.UserId == f.OtherId)).Should().Be(1);
        (await db.Set<QuietInsight>().CountAsync(x => x.UserId == f.OtherId)).Should().Be(1);
        (await db.Boards.FindAsync(f.BoardId)).Should().NotBeNull();
        (await db.Cards.FindAsync(f.CardId)).Should().NotBeNull();
        (await db.Set<ThinkingDeck>().FindAsync(f.CardId))!.ReadLayers().Single().Body.Should().Be("Shared working material");
        (await db.Users.FindAsync(f.UserId))!.IsActive.Should().BeFalse();
    }
}
