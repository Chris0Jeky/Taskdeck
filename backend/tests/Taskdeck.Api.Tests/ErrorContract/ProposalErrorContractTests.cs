using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Api.Contracts;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests.ErrorContract;

/// <summary>
/// Verifies GP-03 error contract compliance for automation proposal endpoints.
/// Every 4xx response must return a structured ApiErrorResponse with
/// non-empty errorCode and message.
/// </summary>
public class ProposalErrorContractTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ProposalErrorContractTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetProposal_NonExistentId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "prop-err-get404");

        var response = await client.GetAsync($"/api/automation/proposals/{Guid.NewGuid()}");

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ApproveProposal_NonExistentId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "prop-err-appr404");

        var response = await client.PostAsync(
            $"/api/automation/proposals/{Guid.NewGuid()}/approve",
            content: null);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task RejectProposal_NonExistentId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "prop-err-rej404");

        var response = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{Guid.NewGuid()}/reject",
            new UpdateProposalStatusDto(null));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ExecuteProposal_NonExistentId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "prop-err-exec404");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/automation/proposals/{Guid.NewGuid()}/execute");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ExecuteProposal_NonExistentIdWithoutIdempotencyKey_ReturnsErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "prop-err-noidemp");

        // Proposal lookup happens before the idempotency header check,
        // so a non-existent ID returns 404 regardless of the missing header.
        // This test verifies the error contract holds for the 404 path.
        var response = await client.PostAsync(
            $"/api/automation/proposals/{Guid.NewGuid()}/execute",
            content: null);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task DismissProposals_EmptyIdsList_Returns400WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "prop-err-dismiss");

        var response = await client.PostAsJsonAsync(
            "/api/automation/proposals/dismiss",
            new DismissProposalsRequest { Ids = new List<Guid>() });

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task GetProposalDiff_NonExistentId_Returns404WithErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "prop-err-diff404");

        var response = await client.GetAsync($"/api/automation/proposals/{Guid.NewGuid()}/diff");

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, ErrorCodes.NotFound);
    }
}
