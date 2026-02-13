using System.Net;
using System.Net.Http.Headers;
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
        var userId = await AuthenticateAsync("automation-create");
        var boardId = await CreateOwnedBoardAsync(userId);
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
        var userId = await AuthenticateAsync("automation-filters");
        var boardId = await CreateOwnedBoardAsync(userId);

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
        var userId = await AuthenticateAsync("automation-approve");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var approveResponse = await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);
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
        var userId = await AuthenticateAsync("automation-reject");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var rejectDto = new UpdateProposalStatusDto(Reason: "Not needed");
        var rejectResponse = await _client.PostAsJsonAsync($"/api/automation/proposals/{proposal.Id}/reject", rejectDto);
        rejectResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var rejectedProposal = await rejectResponse.Content.ReadFromJsonAsync<ProposalDto>();
        rejectedProposal.Should().NotBeNull();
        rejectedProposal!.Status.Should().Be(ProposalStatus.Rejected);
        rejectedProposal.DecidedByUserId.Should().Be(userId);
    }

    [Fact]
    public async Task ExecuteProposal_WhenApproved_ShouldMarkAsApplied()
    {
        var userId = await AuthenticateAsync("automation-exec-applied");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);

        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposal.Id}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var executeResponse = await _client.SendAsync(executeRequest);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executedProposal = await executeResponse.Content.ReadFromJsonAsync<ProposalDto>();
        executedProposal.Should().NotBeNull();
        executedProposal!.Status.Should().Be(ProposalStatus.Applied);
        executedProposal.AppliedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteProposal_WhenNotApproved_ShouldReturnConflict()
    {
        var userId = await AuthenticateAsync("automation-exec-conflict");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposal.Id}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var executeResponse = await _client.SendAsync(executeRequest);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var error = await executeResponse.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("InvalidOperation");
    }

    [Fact]
    public async Task ExecuteProposal_WithoutIdempotencyKey_ShouldReturnBadRequest()
    {
        var userId = await AuthenticateAsync("automation-exec-no-idempotency");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/approve", null);

        var executeResponse = await _client.PostAsync($"/api/automation/proposals/{proposal.Id}/execute", null);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await executeResponse.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("ValidationError");
    }

    [Fact]
    public async Task ApproveProposal_ShouldReturnUnauthorized_WhenNotAuthenticated()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsync($"/api/automation/proposals/{Guid.NewGuid()}/approve", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetProposalDiff_ShouldReturnDiffPreview()
    {
        var userId = await AuthenticateAsync("automation-diff");
        var boardId = await CreateOwnedBoardAsync(userId);
        var proposal = await CreateTestProposal(userId, boardId, RiskLevel.Low);

        var diffResponse = await _client.GetAsync($"/api/automation/proposals/{proposal.Id}/diff");
        diffResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var diffResult = await diffResponse.Content.ReadFromJsonAsync<JsonElement>();
        diffResult.TryGetProperty("diff", out var diff).Should().BeTrue();
    }

    [Fact]
    public async Task GetProposal_ShouldReturnNotFound_WhenProposalDoesNotExist()
    {
        await AuthenticateAsync("automation-get-notfound");

        var response = await _client.GetAsync($"/api/automation/proposals/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task ApproveProposal_ShouldReturnNotFound_WhenProposalDoesNotExist()
    {
        await AuthenticateAsync("automation-approve-notfound");

        var response = await _client.PostAsync($"/api/automation/proposals/{Guid.NewGuid()}/approve", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task CreateProposal_WithEmptySummary_ShouldReturnBadRequest()
    {
        var userId = await AuthenticateAsync("automation-create-invalid");

        var createRequest = new CreateProposalDto(
            SourceType: ProposalSourceType.Chat,
            RequestedByUserId: userId,
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
                    ActionType: "update",
                    TargetType: "board",
                    Parameters: $"{{\"boardId\":\"{boardId}\",\"name\":\"Automated update {Guid.NewGuid():N}\"}}",
                    IdempotencyKey: Guid.NewGuid().ToString(),
                    TargetId: boardId.ToString()
                )
            }
        );

        var response = await _client.PostAsJsonAsync("/api/automation/proposals", createRequest);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProposalDto>())!;
    }

    private async Task<Guid> AuthenticateAsync(string stem)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"{stem}_{suffix}";
        var email = $"{stem}_{suffix}@example.com";
        const string password = "password123";

        var response = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new CreateUserDto(username, email, password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        payload.Should().NotBeNull();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);
        return payload.User.Id;
    }

    private async Task<Guid> CreateOwnedBoardAsync(Guid ownerId)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/import/boards?userId={ownerId}",
            new ImportBoardDto(
                $"automation-board-{Guid.NewGuid():N}",
                null,
                Array.Empty<ImportColumnDto>(),
                Array.Empty<ImportCardDto>(),
                Array.Empty<ImportLabelDto>()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.BoardId.Should().NotBeNull();

        return result.BoardId!.Value;
    }
}
