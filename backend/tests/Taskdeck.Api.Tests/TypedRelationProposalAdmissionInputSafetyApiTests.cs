using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// The producer-neutral relation decorator runs outside the legacy create service. Pin the ordering
/// so malformed operation material continues to use the established 400 contract rather than
/// escaping as a wrapper exception before <c>ProposalOperationInputValidator</c> runs.
/// </summary>
public sealed class TypedRelationProposalAdmissionInputSafetyApiTests(
    HostedWorkerDisabledTestWebApplicationFactory factory)
    : IClassFixture<HostedWorkerDisabledTestWebApplicationFactory>
{
    [Fact]
    public async Task NullOperationBeforeTypedRelation_Returns400WithoutPersistence()
    {
        using var client = factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(
            client,
            $"relation-input-safety-{Guid.NewGuid():N}");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "Relation input safety");
        var board = (await client.GetFromJsonAsync<BoardDetailDto>($"/api/boards/{boardId}"))!;
        var columnId = board.Columns.Single().Id;
        var source = await CreateCardAsync(client, boardId, columnId, "Source");
        var target = await CreateCardAsync(client, boardId, columnId, "Target");
        var before = await CountProposalsAsync();

        var response = await client.PostAsJsonAsync(
            "/api/automation/proposals",
            new
            {
                sourceType = 4,
                requestedByUserId = user.UserId,
                summary = "Malformed relation input",
                riskLevel = 1,
                correlationId = Guid.NewGuid().ToString("N"),
                boardId,
                operations = new object?[]
                {
                    null,
                    new
                    {
                        sequence = 1,
                        actionType = "add-relation",
                        targetType = "card",
                        targetId = source.Id.ToString(),
                        parameters = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            boardId,
                            cardId = source.Id,
                            relatedCardId = target.Id,
                            relationType = "relates-to",
                            expectedRevision = 0
                        }),
                        idempotencyKey = Guid.NewGuid().ToString("N")
                    }
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await CountProposalsAsync()).Should().Be(before);
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

    private async Task<int> CountProposalsAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>()
            .AutomationProposals.CountAsync();
    }
}
