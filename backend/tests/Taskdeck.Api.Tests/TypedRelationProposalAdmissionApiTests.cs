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

/// <summary>
/// Producer-neutral admission contract for typed relation proposals (#3061).
///
/// The web surface posts the same operation vocabulary as MCP/chat through the generic proposal
/// endpoint. A typed relation draft must therefore clear authority, board scope, endpoint and
/// observed-graph validation before any proposal row is persisted, regardless of producer.
/// </summary>
public sealed class TypedRelationProposalAdmissionApiTests(
    HostedWorkerDisabledTestWebApplicationFactory factory)
    : IClassFixture<HostedWorkerDisabledTestWebApplicationFactory>
{
    [Theory]
    [InlineData("stale", HttpStatusCode.Conflict)]
    [InlineData("duplicate", HttpStatusCode.BadRequest)]
    [InlineData("cycle", HttpStatusCode.BadRequest)]
    [InlineData("missing-remove", HttpStatusCode.BadRequest)]
    public async Task WebAdmission_RejectsCurrentGraphFailuresBeforePersistence(
        string failure,
        HttpStatusCode expectedStatus)
    {
        using var client = factory.CreateClient();
        var (user, boardId, columnId) = await SetupAsync(client, $"relation-admission-{failure}");
        var source = await CreateCardAsync(client, boardId, columnId, "Source");
        var target = await CreateCardAsync(client, boardId, columnId, "Target");
        var third = await CreateCardAsync(client, boardId, columnId, "Third");

        CardRelationEdge requested;
        string action;
        long expectedRevision;
        IReadOnlyList<CardRelationEdge> stored;
        switch (failure)
        {
            case "stale":
                requested = new CardRelationEdge(source.Id, target.Id, "blocks");
                action = "add-relation";
                expectedRevision = 0;
                stored = [new CardRelationEdge(source.Id, third.Id, "duplicates")];
                break;
            case "duplicate":
                requested = new CardRelationEdge(source.Id, target.Id, "blocks");
                action = "add-relation";
                expectedRevision = 1;
                stored = [requested];
                break;
            case "cycle":
                requested = new CardRelationEdge(target.Id, source.Id, "blocks");
                action = "add-relation";
                expectedRevision = 1;
                stored = [new CardRelationEdge(source.Id, target.Id, "blocks")];
                break;
            default:
                requested = new CardRelationEdge(source.Id, target.Id, "blocks");
                action = "remove-relation";
                expectedRevision = 0;
                stored = [];
                break;
        }

        if (stored.Count > 0)
            await SeedGraphAsync(boardId, stored);

        var before = await CountProposalsAsync();
        var response = await PostRelationProposalAsync(
            client,
            user.UserId,
            boardId,
            action,
            requested,
            expectedRevision);

        response.StatusCode.Should().Be(expectedStatus, await response.Content.ReadAsStringAsync());
        (await CountProposalsAsync()).Should().Be(before,
            "an invalid typed relation must never become a reviewable draft");
        var persisted = await ReadRelationsAsync(boardId);
        persisted.Should().BeEquivalentTo(stored.Select(CardRelationRules.Normalize));
    }

    [Fact]
    public async Task WebAdmission_RejectsReadOnlyActorBeforePersistence()
    {
        using var ownerClient = factory.CreateClient();
        var (owner, boardId, columnId) = await SetupAsync(ownerClient, "relation-admission-owner");
        var source = await CreateCardAsync(ownerClient, boardId, columnId, "Source");
        var target = await CreateCardAsync(ownerClient, boardId, columnId, "Target");

        using var viewerClient = factory.CreateClient();
        var viewer = await ApiTestHarness.AuthenticateAsync(
            viewerClient,
            $"relation-admission-viewer-{Guid.NewGuid():N}");
        var grant = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{boardId}/access",
            new GrantAccessDto(boardId, viewer.UserId, UserRole.Viewer));
        grant.EnsureSuccessStatusCode();

        var before = await CountProposalsAsync();
        var response = await PostRelationProposalAsync(
            viewerClient,
            owner.UserId, // controller must ignore this and stamp the authenticated viewer
            boardId,
            "add-relation",
            new CardRelationEdge(source.Id, target.Id, "relates-to"),
            expectedRevision: 0);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await CountProposalsAsync()).Should().Be(before);
    }

    [Fact]
    public async Task WebAdmission_RejectsCrossBoardEndpointBeforePersistence()
    {
        using var client = factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(
            client,
            $"relation-admission-cross-board-{Guid.NewGuid():N}");
        var firstBoard = await ApiTestHarness.CreateBoardWithColumnAsync(client, "First relation board");
        var firstDetail = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{firstBoard}"))!;
        var secondBoard = await ApiTestHarness.CreateBoardWithColumnAsync(client, "Second relation board");
        var secondDetail = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{secondBoard}"))!;
        var source = await CreateCardAsync(client, firstBoard, firstDetail.Columns.Single().Id, "Source");
        var foreignTarget = await CreateCardAsync(client, secondBoard, secondDetail.Columns.Single().Id, "Foreign target");

        var before = await CountProposalsAsync();
        var response = await PostRelationProposalAsync(
            client,
            user.UserId,
            firstBoard,
            "add-relation",
            new CardRelationEdge(source.Id, foreignTarget.Id, "relates-to"),
            expectedRevision: 0);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await CountProposalsAsync()).Should().Be(before);
        (await ReadRelationsAsync(firstBoard)).Should().BeEmpty();
        (await ReadRelationsAsync(secondBoard)).Should().BeEmpty();
    }

    [Fact]
    public async Task WebAdmission_PreservesOrderedPlannedCreateSupportWithoutBoardMutation()
    {
        using var client = factory.CreateClient();
        var (user, boardId, columnId) = await SetupAsync(client, "relation-admission-planned");
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var before = await CountProposalsAsync();
        var response = await client.PostAsJsonAsync(
            "/api/automation/proposals",
            new CreateProposalDto(
                ProposalSourceType.Manual,
                user.UserId,
                "Create two cards and relate them",
                RiskLevel.Medium,
                Guid.NewGuid().ToString("N"),
                boardId,
                Operations:
                [
                    CardCreate(0, sourceId, boardId, columnId, "Planned source"),
                    CardCreate(1, targetId, boardId, columnId, "Planned target"),
                    RelationOperation(
                        2,
                        "add-relation",
                        boardId,
                        new CardRelationEdge(sourceId, targetId, "relates-to"),
                        expectedRevision: 0)
                ]));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await CountProposalsAsync()).Should().Be(before + 1);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        (await db.Cards.AnyAsync(card => card.Id == sourceId || card.Id == targetId)).Should().BeFalse(
            "admission validates the planned sequence but remains proposal-only");
        (await ReadRelationsAsync(boardId)).Should().BeEmpty();
    }

    private async Task<(TestUserContext User, Guid BoardId, Guid ColumnId)> SetupAsync(
        HttpClient client,
        string stem)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = await ApiTestHarness.AuthenticateAsync(client, $"{stem}-{suffix}");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, $"{stem} board {suffix}");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        return (user, boardId, board.Columns.Single().Id);
    }

    private static async Task<CardDto> CreateCardAsync(
        HttpClient client,
        Guid boardId,
        Guid columnId,
        string title)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new CreateCardDto(boardId, columnId, title, null, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CardDto>())!;
    }

    private async Task SeedGraphAsync(Guid boardId, IReadOnlyList<CardRelationEdge> relations)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var graph = new BoardDependencies(boardId);
        graph.ReplaceRelations(relations);
        db.Add(graph);
        await db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<CardRelationEdge>> ReadRelationsAsync(Guid boardId)
    {
        using var scope = factory.Services.CreateScope();
        var graph = await scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>()
            .Set<BoardDependencies>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.BoardId == boardId);
        return graph?.ReadRelations() ?? [];
    }

    private async Task<int> CountProposalsAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>()
            .AutomationProposals.CountAsync();
    }

    private static Task<HttpResponseMessage> PostRelationProposalAsync(
        HttpClient client,
        Guid requestedByUserId,
        Guid boardId,
        string action,
        CardRelationEdge relation,
        long expectedRevision) =>
        client.PostAsJsonAsync(
            "/api/automation/proposals",
            new CreateProposalDto(
                ProposalSourceType.Manual,
                requestedByUserId,
                "Typed relation admission",
                RiskLevel.Medium,
                Guid.NewGuid().ToString("N"),
                boardId,
                Operations:
                [
                    RelationOperation(
                        0,
                        action,
                        boardId,
                        relation,
                        expectedRevision)
                ]));

    private static CreateProposalOperationDto RelationOperation(
        int sequence,
        string action,
        Guid boardId,
        CardRelationEdge relation,
        long expectedRevision) =>
        new(
            sequence,
            action,
            "card",
            JsonSerializer.Serialize(new
            {
                boardId,
                cardId = relation.SourceCardId,
                relatedCardId = relation.TargetCardId,
                relationType = relation.RelationType,
                expectedRevision
            }),
            Guid.NewGuid().ToString("N"),
            relation.SourceCardId.ToString());

    private static CreateProposalOperationDto CardCreate(
        int sequence,
        Guid cardId,
        Guid boardId,
        Guid columnId,
        string title) =>
        new(
            sequence,
            "create",
            "card",
            JsonSerializer.Serialize(new { boardId, columnId, title }),
            Guid.NewGuid().ToString("N"),
            cardId.ToString());
}
