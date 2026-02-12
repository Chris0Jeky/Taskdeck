using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Api.Tests;

public class AutomationProposalsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AutomationProposalsApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateProposal_ThenGetProposal_ShouldReturnCreatedProposal()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString();

        var createRequest = new CreateProposalDto(
            SourceType: ProposalSourceType.Chat,
            RequestedByUserId: userId,
            Summary: "Test automation proposal",
            RiskLevel: RiskLevel.Low,
            CorrelationId: correlationId,
            BoardId: boardId,
            Operations: new List<CreateProposalOperationDto>
            {
                new(
                    Sequence: 1,
                    ActionType: "CreateCard",
                    TargetType: "Card",
                    Parameters: "{\"title\":\"Test Card\"}",
                    IdempotencyKey: Guid.NewGuid().ToString()
                )
            }
        );

        var createResponse = await _client.PostAsJsonAsync("/api/automation/proposals", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdProposal = await createResponse.Content.ReadFromJsonAsync<ProposalDto>();
        createdProposal.Should().NotBeNull();
        createdProposal!.Summary.Should().Be(createRequest.Summary);
        createdProposal.Status.Should().Be(ProposalStatus.PendingReview);
        createdProposal.RiskLevel.Should().Be(RiskLevel.Low);
        createdProposal.Operations.Should().HaveCount(1);

        var getResponse = await _client.GetAsync($"/api/automation/proposals/{createdProposal.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var retrievedProposal = await getResponse.Content.ReadFromJsonAsync<ProposalDto>();
        retrievedProposal.Should().NotBeNull();
        retrievedProposal!.Id.Should().Be(createdProposal.Id);
        retrievedProposal.Summary.Should().Be(createRequest.Summary);
    }

    [Fact]
    public async Task GetProposals_WithFilters_ShouldReturnFilteredResults()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();

        var proposal1 = await CreateTestProposal(userId, boardId, RiskLevel.Low);
        var proposal2 = await CreateTestProposal(userId, boardId, RiskLevel.High);

        var response = await _client.GetAsync($"/api/automation/proposals?boardId={boardId}&status={ProposalStatus.PendingReview}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var proposals = await response.Content.ReadFromJsonAsync<List<ProposalDto>>();
        proposals.Should().NotBeNull();
        proposals.Should().NotBeEmpty();
        proposals.Should().Contain(p => p.Id == proposal1.Id);
        proposals.Should().Contain(p => p.Id == proposal2.Id);
    }

    [Fact]
    public async Task ApproveProposal_ShouldUpdateStatus()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var approveResponse = await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve?decidedByUserId={userId}", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var approvedProposal = await approveResponse.Content.ReadFromJsonAsync<ProposalDto>();
        approvedProposal.Should().NotBeNull();
        approvedProposal!.Status.Should().Be(ProposalStatus.Approved);
        approvedProposal.DecidedByUserId.Should().Be(userId);
        approvedProposal.DecidedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RejectProposal_ShouldUpdateStatus()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var rejectDto = new UpdateProposalStatusDto(Reason: "Not needed");
        var rejectResponse = await _client.PostAsJsonAsync($"/api/automation/proposals/{proposal.Id}/reject?decidedByUserId={userId}", rejectDto);
        rejectResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var rejectedProposal = await rejectResponse.Content.ReadFromJsonAsync<ProposalDto>();
        rejectedProposal.Should().NotBeNull();
        rejectedProposal!.Status.Should().Be(ProposalStatus.Rejected);
        rejectedProposal.DecidedByUserId.Should().Be(userId);
    }

    [Fact]
    public async Task ExecuteProposal_WhenApproved_ShouldMarkAsApplied()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve?decidedByUserId={userId}", null);

        var executeResponse = await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/execute", null);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executedProposal = await executeResponse.Content.ReadFromJsonAsync<ProposalDto>();
        executedProposal.Should().NotBeNull();
        executedProposal!.Status.Should().Be(ProposalStatus.Applied);
        executedProposal.AppliedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteProposal_WhenNotApproved_ShouldReturnConflict()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var executeResponse = await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/execute", null);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var error = await executeResponse.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("Conflict");
    }

    [Fact]
    public async Task GetProposalDiff_ShouldReturnDiffPreview()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var diffResponse = await _client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        diffResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var diffResult = await diffResponse.Content.ReadFromJsonAsync<JsonElement>();
        diffResult.TryGetProperty("diff", out var diff).Should().BeTrue();
    }

    [Fact]
    public async Task GetProposal_ShouldReturnNotFound_WhenProposalDoesNotExist()
    {
        var response = await _client.GetAsync($"/api/automation/proposals/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task ApproveProposal_ShouldReturnNotFound_WhenProposalDoesNotExist()
    {
        var response = await _client.PostAsync($"/api/automation/proposals/{Guid.NewGuid()}/approve?decidedByUserId={Guid.NewGuid()}", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task CreateProposal_WithEmptySummary_ShouldReturnBadRequest()
    {
        var createRequest = new CreateProposalDto(
            SourceType: ProposalSourceType.Chat,
            RequestedByUserId: Guid.NewGuid(),
            Summary: string.Empty,
            RiskLevel: RiskLevel.Low,
            CorrelationId: Guid.NewGuid().ToString()
        );

        var response = await _client.PostAsJsonAsync("/api/automation/proposals", createRequest);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("ValidationError");
    }

    private async Task<ProposalDto> CreateTestProposal(Guid userId, Guid boardId, RiskLevel riskLevel)
    {
        var createRequest = new CreateProposalDto(
            SourceType: ProposalSourceType.Chat,
            RequestedByUserId: userId,
            Summary: $"Test proposal {Guid.NewGuid()}",
            RiskLevel: riskLevel,
            CorrelationId: Guid.NewGuid().ToString(),
            BoardId: boardId,
            Operations: new List<CreateProposalOperationDto>
            {
                new(
                    Sequence: 1,
                    ActionType: "CreateCard",
                    TargetType: "Card",
                    Parameters: "{\"title\":\"Test\"}",
                    IdempotencyKey: Guid.NewGuid().ToString()
                )
            }
        );

        var response = await _client.PostAsJsonAsync("/api/automation/proposals", createRequest);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProposalDto>())!;
    }
}
