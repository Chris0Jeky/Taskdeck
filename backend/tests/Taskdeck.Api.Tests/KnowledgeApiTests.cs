using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Api.Tests;

public class KnowledgeApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public KnowledgeApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateDocument_ShouldReturnCreated()
    {
        await AuthenticateAsync("know-create");

        var dto = new CreateKnowledgeDocumentDto(
            "Test Document",
            "This is test content for the knowledge document.",
            KnowledgeSourceType.Manual);

        var response = await _client.PostAsJsonAsync("/api/knowledge", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var document = await response.Content.ReadFromJsonAsync<KnowledgeDocumentDto>();
        document.Should().NotBeNull();
        document!.Title.Should().Be("Test Document");
        document.Content.Should().Be("This is test content for the knowledge document.");
        document.SourceType.Should().Be(KnowledgeSourceType.Manual);
        document.IsArchived.Should().BeFalse();
    }

    [Fact]
    public async Task GetDocument_ShouldReturnDocument()
    {
        await AuthenticateAsync("know-get");

        var createDto = new CreateKnowledgeDocumentDto(
            "Get Test",
            "Content for get test.",
            KnowledgeSourceType.Manual);

        var createResponse = await _client.PostAsJsonAsync("/api/knowledge", createDto);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<KnowledgeDocumentDto>();
        created.Should().NotBeNull();

        var getResponse = await _client.GetAsync($"/api/knowledge/{created!.Id}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var document = await getResponse.Content.ReadFromJsonAsync<KnowledgeDocumentDto>();
        document.Should().NotBeNull();
        document!.Id.Should().Be(created.Id);
        document.Title.Should().Be("Get Test");
    }

    [Fact]
    public async Task ListDocuments_ShouldReturnDocuments()
    {
        await AuthenticateAsync("know-list");

        var dto = new CreateKnowledgeDocumentDto(
            "List Test",
            "Content for list test.",
            KnowledgeSourceType.Manual);

        var createResponse = await _client.PostAsJsonAsync("/api/knowledge", dto);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var listResponse = await _client.GetAsync("/api/knowledge");

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var documents = await listResponse.Content.ReadFromJsonAsync<List<KnowledgeDocumentDto>>();
        documents.Should().NotBeNull();
        documents!.Should().Contain(d => d.Title == "List Test");
    }

    [Fact]
    public async Task UpdateDocument_ShouldReturnUpdated()
    {
        await AuthenticateAsync("know-update");

        var createDto = new CreateKnowledgeDocumentDto(
            "Before Update",
            "Original content.",
            KnowledgeSourceType.Manual);

        var createResponse = await _client.PostAsJsonAsync("/api/knowledge", createDto);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<KnowledgeDocumentDto>();
        created.Should().NotBeNull();

        var updateDto = new UpdateKnowledgeDocumentDto("After Update", "Updated content.", "tag1,tag2");
        var updateResponse = await _client.PutAsJsonAsync($"/api/knowledge/{created!.Id}", updateDto);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<KnowledgeDocumentDto>();
        updated.Should().NotBeNull();
        updated!.Title.Should().Be("After Update");
        updated.Content.Should().Be("Updated content.");
        updated.Tags.Should().Be("tag1,tag2");
    }

    [Fact]
    public async Task ArchiveDocument_ShouldReturnNoContent()
    {
        await AuthenticateAsync("know-archive");

        var createDto = new CreateKnowledgeDocumentDto(
            "To Archive",
            "Content to archive.",
            KnowledgeSourceType.Manual);

        var createResponse = await _client.PostAsJsonAsync("/api/knowledge", createDto);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<KnowledgeDocumentDto>();
        created.Should().NotBeNull();

        var archiveResponse = await _client.DeleteAsync($"/api/knowledge/{created!.Id}");

        archiveResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify the document is archived by retrieving it
        var getResponse = await _client.GetAsync($"/api/knowledge/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var archived = await getResponse.Content.ReadFromJsonAsync<KnowledgeDocumentDto>();
        archived.Should().NotBeNull();
        archived!.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task SearchDocuments_ShouldReturnResults()
    {
        await AuthenticateAsync("know-search");

        var createDto = new CreateKnowledgeDocumentDto(
            "Searchable Document",
            "This document contains unique searchable keyword zephyr for testing.",
            KnowledgeSourceType.Manual);

        var createResponse = await _client.PostAsJsonAsync("/api/knowledge", createDto);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var searchResponse = await _client.GetAsync("/api/knowledge/search?q=zephyr");

        searchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var results = await searchResponse.Content.ReadFromJsonAsync<List<KnowledgeSearchResultDto>>();
        results.Should().NotBeNull();
        results!.Should().Contain(r => r.Title == "Searchable Document");
    }

    [Fact]
    public async Task SearchDocuments_EmptyQuery_ShouldReturnValidationError()
    {
        await AuthenticateAsync("know-search-empty");

        var searchResponse = await _client.GetAsync("/api/knowledge/search?q=");

        await ApiTestHarness.AssertErrorContractAsync(searchResponse, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetDocument_DifferentUser_ShouldReturnForbidden()
    {
        using var ownerClient = _factory.CreateClient();
        using var outsiderClient = _factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(ownerClient, "know-authz-owner");
        await ApiTestHarness.AuthenticateAsync(outsiderClient, "know-authz-outsider");

        var createDto = new CreateKnowledgeDocumentDto(
            "Private Document",
            "Private content only owner can see.",
            KnowledgeSourceType.Manual);

        var createResponse = await ownerClient.PostAsJsonAsync("/api/knowledge", createDto);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<KnowledgeDocumentDto>();
        created.Should().NotBeNull();

        var getResponse = await outsiderClient.GetAsync($"/api/knowledge/{created!.Id}");

        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(getResponse);
    }

    [Fact]
    public async Task UpdateDocument_DifferentUser_ShouldReturnForbidden()
    {
        using var ownerClient = _factory.CreateClient();
        using var outsiderClient = _factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(ownerClient, "know-update-owner");
        await ApiTestHarness.AuthenticateAsync(outsiderClient, "know-update-outsider");

        var createDto = new CreateKnowledgeDocumentDto(
            "Private Doc Update",
            "Private content.",
            KnowledgeSourceType.Manual);

        var createResponse = await ownerClient.PostAsJsonAsync("/api/knowledge", createDto);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<KnowledgeDocumentDto>();
        created.Should().NotBeNull();

        var updateDto = new UpdateKnowledgeDocumentDto("Hacked Title", "Hacked content.");
        var updateResponse = await outsiderClient.PutAsJsonAsync($"/api/knowledge/{created!.Id}", updateDto);

        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(updateResponse);
    }

    [Fact]
    public async Task ArchiveDocument_DifferentUser_ShouldReturnForbidden()
    {
        using var ownerClient = _factory.CreateClient();
        using var outsiderClient = _factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(ownerClient, "know-archive-owner");
        await ApiTestHarness.AuthenticateAsync(outsiderClient, "know-archive-outsider");

        var createDto = new CreateKnowledgeDocumentDto(
            "Private Doc Archive",
            "Private content.",
            KnowledgeSourceType.Manual);

        var createResponse = await ownerClient.PostAsJsonAsync("/api/knowledge", createDto);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<KnowledgeDocumentDto>();
        created.Should().NotBeNull();

        var archiveResponse = await outsiderClient.DeleteAsync($"/api/knowledge/{created!.Id}");

        await ApiTestHarness.AssertNotFoundOrForbiddenAsync(archiveResponse);
    }

    [Fact]
    public async Task Endpoints_RequireAuthentication()
    {
        using var unauthClient = _factory.CreateClient();

        await ApiTestHarness.AssertUnauthorizedAsync(
            await unauthClient.GetAsync("/api/knowledge"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await unauthClient.GetAsync($"/api/knowledge/{Guid.NewGuid()}"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await unauthClient.PostAsJsonAsync("/api/knowledge",
                new CreateKnowledgeDocumentDto("Test", "Content", KnowledgeSourceType.Manual)));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await unauthClient.PutAsJsonAsync($"/api/knowledge/{Guid.NewGuid()}",
                new UpdateKnowledgeDocumentDto("Test", "Content")));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await unauthClient.DeleteAsync($"/api/knowledge/{Guid.NewGuid()}"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await unauthClient.GetAsync("/api/knowledge/search?q=test"));
    }

    private async Task<Guid> AuthenticateAsync(string stem)
    {
        var context = await ApiTestHarness.AuthenticateAsync(_client, stem);
        return context.UserId;
    }
}
