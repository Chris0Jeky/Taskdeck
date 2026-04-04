using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Api.Tests;

public class AgentRunsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AgentRunsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, AgentProfileDto Profile)> SetupAgentAsync(string stem)
    {
        var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, stem);
        var createResponse = await client.PostAsJsonAsync("/api/agents",
            new CreateAgentProfileDto($"{stem}-agent", "triage", AgentScopeType.Workspace));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var profile = await createResponse.Content.ReadFromJsonAsync<AgentProfileDto>();
        profile.Should().NotBeNull();
        return (client, profile!);
    }

    [Fact]
    public async Task CreateRun_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();
        var agentId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync($"/api/agents/{agentId}/runs",
            new CreateAgentRunDto("test objective"));

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task ListRuns_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();
        var agentId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/agents/{agentId}/runs");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task GetRun_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();
        var agentId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/agents/{agentId}/runs/{runId}");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task CreateRun_ShouldReturnCreated_ForOwnAgent()
    {
        var (client, profile) = await SetupAgentAsync("run-create");

        var response = await client.PostAsJsonAsync($"/api/agents/{profile.Id}/runs",
            new CreateAgentRunDto("Triage inbox items"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var run = await response.Content.ReadFromJsonAsync<AgentRunDto>();
        run.Should().NotBeNull();
        run!.AgentProfileId.Should().Be(profile.Id);
        run.Objective.Should().Be("Triage inbox items");
    }

    [Fact]
    public async Task ListRuns_ShouldReturnOk_ForOwnAgent()
    {
        var (client, profile) = await SetupAgentAsync("run-list");

        var response = await client.GetAsync($"/api/agents/{profile.Id}/runs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetRun_NonExistent_ShouldReturn404()
    {
        var (client, profile) = await SetupAgentAsync("run-notfound");

        var response = await client.GetAsync($"/api/agents/{profile.Id}/runs/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, "NotFound");
    }

    [Fact]
    public async Task CrossUserIsolation_ShouldPreventAccessToOtherUsersRuns()
    {
        // User A creates an agent and a run (inline to properly dispose client)
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "run-owner");
        var createProfileResp = await clientA.PostAsJsonAsync("/api/agents",
            new CreateAgentProfileDto("run-owner-agent", "triage", AgentScopeType.Workspace));
        createProfileResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var profileA = await createProfileResp.Content.ReadFromJsonAsync<AgentProfileDto>();
        profileA.Should().NotBeNull();

        var createRunResponse = await clientA.PostAsJsonAsync($"/api/agents/{profileA!.Id}/runs",
            new CreateAgentRunDto("Owner run"));
        createRunResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var run = await createRunResponse.Content.ReadFromJsonAsync<AgentRunDto>();
        run.Should().NotBeNull();

        // User B tries to access
        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "run-outsider");

        var listResponse = await clientB.GetAsync($"/api/agents/{profileA.Id}/runs");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(listResponse);

        var getResponse = await clientB.GetAsync($"/api/agents/{profileA.Id}/runs/{run!.Id}");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(getResponse);
    }
}
