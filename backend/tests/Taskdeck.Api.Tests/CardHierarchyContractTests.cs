using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class CardHierarchyContractTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Theory]
    [InlineData("duplicate")]
    [InlineData("multiple")]
    public async Task HierarchyProposalRejectsDuplicateCreateIdsAndMultipleGraphChanges(string shape)
    {
        using var client = factory.CreateClient(); var actor = await ApiTestHarness.AuthenticateAsync(client, "hierarchy-shape");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "Hierarchy shape");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards", new CreateCardDto(boardId, board.Columns[0].Id, "Parent", null, null, null));
        var card = (await response.Content.ReadFromJsonAsync<CardDto>())!;
        var operations = new List<CreateProposalOperationDto> {
            new(0, "create", "card", JsonSerializer.Serialize(new { boardId, columnId = card.ColumnId, title = "Child", parentCardId = card.Id }), Guid.NewGuid().ToString(), shape == "duplicate" ? card.Id.ToString() : Guid.NewGuid().ToString())
        };
        if (shape == "multiple") operations.Add(new(1, "update", "card", JsonSerializer.Serialize(new { cardId = card.Id, clearParent = true, expectedUpdatedAt = card.UpdatedAt }), Guid.NewGuid().ToString(), card.Id.ToString()));
        var created = await client.PostAsJsonAsync("/api/automation/proposals", new CreateProposalDto(ProposalSourceType.Manual, actor.UserId, "Bad hierarchy shape", RiskLevel.Low, Guid.NewGuid().ToString(), boardId, Operations: operations));
        created.EnsureSuccessStatusCode(); var proposal = (await created.Content.ReadFromJsonAsync<ProposalDto>())!;
        (await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{boardId}/cards"))!.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParentAssignmentAndRemovalProposalsShowActualChangeAndOnlyApplyAfterApproval(bool clear)
    {
        using var client = factory.CreateClient();
        var actor = await ApiTestHarness.AuthenticateAsync(client, "hierarchy-parent-proposal");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "Parent proposals");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        async Task<CardDto> Create(string title, Guid? parentId = null)
        {
            var result = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards", new CreateCardDto(boardId, board.Columns[0].Id, title, null, null, null, ParentCardId: parentId));
            result.EnsureSuccessStatusCode(); return (await result.Content.ReadFromJsonAsync<CardDto>())!;
        }
        var parent = await Create("Explicit parent"); var child = await Create("Explicit child", clear ? parent.Id : null);
        var parameters = new Dictionary<string, object> { ["cardId"] = child.Id, ["expectedUpdatedAt"] = child.UpdatedAt };
        if (clear) parameters["clearParent"] = true; else parameters["parentCardId"] = parent.Id;
        var created = await client.PostAsJsonAsync("/api/automation/proposals", new CreateProposalDto(
            ProposalSourceType.Manual, actor.UserId, "Change parent", RiskLevel.Low, Guid.NewGuid().ToString(), boardId,
            Operations: [new(0, "update", "card", JsonSerializer.Serialize(parameters), Guid.NewGuid().ToString(), child.Id.ToString())]));
        created.EnsureSuccessStatusCode(); var proposal = (await created.Content.ReadFromJsonAsync<ProposalDto>())!;
        (await client.GetStringAsync($"/api/automation/proposals/{proposal.Id}/diff")).Should().Contain(parent.Title).And.Contain("none");
        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null)).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{child.Id}"))!.ParentCardId.Should().Be(child.ParentCardId);
        using var apply = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposal.Id}/execute");
        apply.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await client.SendAsync(apply); response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync<ProposalDto>())!.Status.Should().Be(ProposalStatus.Applied);
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{child.Id}"))!.ParentCardId.Should().Be(clear ? null : parent.Id);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.AuditLogs.AnyAsync(row => row.EntityId == child.Id && row.Changes != null && row.Changes.Contains("ParentCardId:"))).Should().BeTrue();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("cycle")]
    [InlineData("valid")]
    public async Task ImportRemapsChildBeforeParentAndRejectsBadGraphsAtomically(string shape)
    {
        using var client = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "hierarchy-import");
        var parent = Guid.NewGuid(); var child = Guid.NewGuid();
        var cards = new[] {
            new ImportCardDto("Child first", "Keep", "Original", 0, null, [], SourceId: child, ParentCardId: shape == "missing" ? Guid.NewGuid() : parent),
            new ImportCardDto("Parent second", "Keep", "Original", 1, null, [], SourceId: shape == "duplicate" ? child : parent, ParentCardId: shape == "cycle" ? child : null),
        };
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var before = await db.Boards.CountAsync();
        var response = await client.PostAsJsonAsync("/api/import/boards", new ImportBoardDto("Hierarchy import", null, [new("Original", 0, null)], cards, []));
        if (shape != "valid")
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
            (await db.Boards.CountAsync()).Should().Be(before);
            return;
        }
        response.EnsureSuccessStatusCode();
        var imported = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        var saved = (await client.GetFromJsonAsync<List<CardDto>>($"/api/boards/{imported.BoardId}/cards"))!;
        var savedParent = saved.Single(card => card.Title == "Parent second");
        savedParent.Id.Should().NotBe(parent);
        saved.Single(card => card.Title == "Child first").ParentCardId.Should().Be(savedParent.Id);
        var exported = await client.GetStringAsync($"/api/export/boards/{imported.BoardId}/json");
        using var document = JsonDocument.Parse(exported);
        document.RootElement.GetProperty("version").GetInt32().Should().Be(3);
        var again = await client.PostAsJsonAsync("/api/import/boards/json", document.RootElement);
        again.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProposalPreviewApprovalAndApplyPinEveryDetach(bool stale)
    {
        using var client = factory.CreateClient();
        var actor = await ApiTestHarness.AuthenticateAsync(client, "hierarchy-proposal");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "hierarchy-proposal");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        async Task<CardDto> Create(string title, Guid? parent = null)
        {
            var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/cards", new CreateCardDto(boardId, board.Columns[0].Id, title, null, null, null, ParentCardId: parent));
            response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<CardDto>())!;
        }
        var parent = await Create("Proposal parent"); var child = await Create("Visible affected child", parent.Id);
        var preview = (await client.GetFromJsonAsync<CardDetachPreviewDto>($"/api/boards/{boardId}/cards/{parent.Id}/detach-preview"))!;
        var created = await client.PostAsJsonAsync("/api/automation/proposals", new CreateProposalDto(
            ProposalSourceType.Manual, actor.UserId, "Archive reviewed parent", RiskLevel.High, Guid.NewGuid().ToString(), boardId,
            Operations: [new(0, "archive-lifecycle", "card", JsonSerializer.Serialize(new { cardId = parent.Id, expectedUpdatedAt = preview.ExpectedUpdatedAt,
                expectedChildrenFingerprint = preview.ExpectedChildrenFingerprint }), Guid.NewGuid().ToString(), parent.Id.ToString())]));
        created.EnsureSuccessStatusCode(); var proposal = (await created.Content.ReadFromJsonAsync<ProposalDto>())!;
        var diff = await client.GetStringAsync($"/api/automation/proposals/{proposal.Id}/diff");
        diff.Should().Contain(child.Id.ToString()).And.Contain(child.Title).And.Contain("none");
        async Task<HttpResponseMessage> Apply()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposal.Id}/execute");
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); return await client.SendAsync(request);
        }
        (await Apply()).IsSuccessStatusCode.Should().BeFalse();
        (await client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null)).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{parent.Id}"))!.IsArchived.Should().BeFalse();
        if (stale) await Create("Child after approval", parent.Id);
        var applied = await Apply();
        if (stale)
        {
            (await client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff")).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{child.Id}"))!.ParentCardId.Should().Be(parent.Id);
            (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{parent.Id}"))!.IsArchived.Should().BeFalse();
        }
        else
        {
            applied.EnsureSuccessStatusCode();
            (await applied.Content.ReadFromJsonAsync<ProposalDto>())!.Status.Should().Be(ProposalStatus.Applied);
            (await client.GetFromJsonAsync<CardDto>($"/api/boards/{boardId}/cards/{child.Id}"))!.ParentCardId.Should().BeNull();
        }
    }
}
