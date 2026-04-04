using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests.ErrorContract;

/// <summary>
/// Verifies GP-03 error contract compliance for capture endpoints.
/// Every 4xx response must return a structured ApiErrorResponse with
/// non-empty errorCode and message.
/// </summary>
public class CaptureErrorContractTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CaptureErrorContractTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateCapture_EmptyText_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "cap-err-empty");

        var response = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, string.Empty));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task CreateCapture_WhitespaceText_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "cap-err-ws");

        var response = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "   "));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task CreateCapture_NoBoardContext_Succeeds()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "cap-err-noboard");

        var response = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "A capture without board context"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task GetCapture_NonExistentId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "cap-err-404");

        var response = await client.GetAsync($"/api/capture/items/{Guid.NewGuid()}");

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task IgnoreCapture_NonExistentId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "cap-err-ign404");

        var response = await client.PostAsync(
            $"/api/capture/items/{Guid.NewGuid()}/ignore",
            content: null);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task CancelCapture_NonExistentId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "cap-err-cancel404");

        var response = await client.PostAsync(
            $"/api/capture/items/{Guid.NewGuid()}/cancel",
            content: null);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ListCaptures_InvalidStatus_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "cap-err-status");

        var response = await client.GetAsync("/api/capture/items?status=InvalidStatusValue");

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task TriageCapture_NonExistentId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "cap-err-triage404");

        var response = await client.PostAsync(
            $"/api/capture/items/{Guid.NewGuid()}/triage",
            content: null);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }
}
