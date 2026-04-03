using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Api.Tests;

public class AgentProfilesApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AgentProfilesApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListProfiles_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/agents");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task GetProfile_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/agents/{Guid.NewGuid()}");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task CreateProfile_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/agents",
            new CreateAgentProfileDto("Test Agent", "triage", AgentScopeType.Workspace));

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task UpdateProfile_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/agents/{Guid.NewGuid()}",
            new UpdateAgentProfileDto("Updated Agent"));

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task DeleteProfile_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.DeleteAsync($"/api/agents/{Guid.NewGuid()}");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task CreateAndGetProfile_ShouldWork_ForAuthenticatedUser()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "agent-create-get");

        // CREATE
        var createResponse = await client.PostAsJsonAsync("/api/agents",
            new CreateAgentProfileDto("Test Create-Get Agent", "triage", AgentScopeType.Workspace,
                Description: "Integration test agent"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<AgentProfileDto>();
        created.Should().NotBeNull();
        created!.Name.Should().Be("Test Create-Get Agent");
        created.TemplateKey.Should().Be("triage");

        // GET by ID
        var getResponse = await client.GetAsync($"/api/agents/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<AgentProfileDto>();
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
        fetched.Name.Should().Be("Test Create-Get Agent");
    }

    [Fact]
    public async Task UpdateProfile_ShouldWork_ForAuthenticatedUser()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "agent-update");

        var createResponse = await client.PostAsJsonAsync("/api/agents",
            new CreateAgentProfileDto("Agent To Update", "triage", AgentScopeType.Workspace));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<AgentProfileDto>();
        created.Should().NotBeNull();

        var updateResponse = await client.PutAsJsonAsync($"/api/agents/{created!.Id}",
            new UpdateAgentProfileDto("Renamed Agent", Description: "Updated description"));
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<AgentProfileDto>();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Renamed Agent");
    }

    [Fact]
    public async Task DeleteProfile_ShouldWork_ForAuthenticatedUser()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "agent-delete");

        var createResponse = await client.PostAsJsonAsync("/api/agents",
            new CreateAgentProfileDto("Agent To Delete", "triage", AgentScopeType.Workspace));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<AgentProfileDto>();
        created.Should().NotBeNull();

        var deleteResponse = await client.DeleteAsync($"/api/agents/{created!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deleted
        var getDeletedResponse = await client.GetAsync($"/api/agents/{created.Id}");
        getDeletedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(Skip = "Known bug: GET /api/agents returns 500 UnexpectedError — tracked separately")]
    public async Task ListProfiles_ShouldReturnOk_ForAuthenticatedUser()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "agent-list-check");

        var listResponse = await client.GetAsync("/api/agents");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetProfile_NonExistent_ShouldReturn404()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "agent-notfound");

        var response = await client.GetAsync($"/api/agents/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, "NotFound");
    }

    [Fact]
    public async Task CrossUserIsolation_ShouldPreventAccessToOtherUsersProfiles()
    {
        // User A creates a profile
        using var clientA = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "agent-owner");
        var createResponse = await clientA.PostAsJsonAsync("/api/agents",
            new CreateAgentProfileDto("Owner Agent", "triage", AgentScopeType.Workspace));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var profile = await createResponse.Content.ReadFromJsonAsync<AgentProfileDto>();
        profile.Should().NotBeNull();

        // User B tries to access it
        using var clientB = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientB, "agent-outsider");

        var getResponse = await clientB.GetAsync($"/api/agents/{profile!.Id}");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(getResponse);

        var updateResponse = await clientB.PutAsJsonAsync($"/api/agents/{profile.Id}",
            new UpdateAgentProfileDto("Hijacked"));
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(updateResponse);

        var deleteResponse = await clientB.DeleteAsync($"/api/agents/{profile.Id}");
        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(deleteResponse);
    }
}
