using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class ProposalConflictUnevaluableOperationApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ProposalConflictUnevaluableOperationApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetConflicts_HistoricalMalformedStoredOperation_SurfacesUnevaluableWarning()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "historical-malformed-conflict");
        var boardId = await ApiTestHarness.CreateBoardWithColumnAsync(client, "historical-malformed-conflict");
        var proposal = new AutomationProposal(
            ProposalSourceType.Manual,
            user.UserId,
            "Historical malformed operation",
            RiskLevel.Low,
            Guid.NewGuid().ToString(),
            boardId);
        proposal.AddOperation(new AutomationProposalOperation(
            proposal.Id,
            sequence: 0,
            actionType: "create",
            targetType: "card",
            parameters: "not-json{",
            idempotencyKey: Guid.NewGuid().ToString()));

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
            dbContext.AutomationProposals.Add(proposal);
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/automation/proposals/{proposal.Id}/conflicts");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var rows = await response.Content.ReadFromJsonAsync<List<ConflictRowDto>>();
        rows.Should().NotBeNull();
        var warning = rows!.Should().ContainSingle(row =>
            row.Tone == ConflictTone.Warn && row.Key == "unable-to-evaluate-operation").Subject;
        warning.Value.Should().Be("1 proposal operation could not be evaluated");
        rows.Should().NotContain(row => row.Tone == ConflictTone.Ok);
        rows.Should().NotContain(row => row.Key == "status");
    }
}
