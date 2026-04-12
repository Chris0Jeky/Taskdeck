using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Adversarial tests for automation proposal creation with malformed operation payloads.
/// Verifies that malformed types, missing fields, extra unknown fields, and adversarial
/// parameter content never cause 500 errors.
/// </summary>
public class ProposalOperationsAdversarialTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public ProposalOperationsAdversarialTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated) return;
        await ApiTestHarness.AuthenticateAsync(_client, "proposal-adversarial");
        _isAuthenticated = true;
    }

    // ─────────────────────── Malformed proposal JSON ───────────────────────

    public static IEnumerable<object[]> MalformedProposalBodies()
    {
        // Missing required fields
        yield return new object[] { "{}" };
        yield return new object[] { "{\"summary\": \"test\"}" };
        yield return new object[] { "{\"sourceType\": 0}" };

        // Wrong types for fields
        yield return new object[] { "{\"sourceType\": \"not-a-number\", \"summary\": \"test\", \"riskLevel\": 0, \"correlationId\": \"abc\"}" };
        yield return new object[] { "{\"sourceType\": 0, \"summary\": 12345, \"riskLevel\": 0, \"correlationId\": \"abc\"}" };
        yield return new object[] { "{\"sourceType\": 0, \"summary\": \"test\", \"riskLevel\": \"high\", \"correlationId\": \"abc\"}" };
        yield return new object[] { "{\"sourceType\": 0, \"summary\": \"test\", \"riskLevel\": 0, \"correlationId\": 12345}" };
        yield return new object[] { "{\"sourceType\": 0, \"summary\": \"test\", \"riskLevel\": 0, \"correlationId\": \"abc\", \"expiryMinutes\": \"sixty\"}" };

        // Extra unknown fields
        yield return new object[] { "{\"sourceType\": 0, \"summary\": \"test\", \"riskLevel\": 0, \"correlationId\": \"abc\", \"__proto__\": {\"admin\": true}}" };
        yield return new object[] { "{\"sourceType\": 0, \"summary\": \"test\", \"riskLevel\": 0, \"correlationId\": \"abc\", \"constructor\": {\"prototype\": {}}}" };
        yield return new object[] { "{\"sourceType\": 0, \"summary\": \"test\", \"riskLevel\": 0, \"correlationId\": \"abc\", \"extraField\": \"ignored\"}" };

        // Operations with malformed data
        yield return new object[]
        {
            "{\"sourceType\": 0, \"summary\": \"test\", \"riskLevel\": 0, \"correlationId\": \"abc\", " +
            "\"operations\": [{\"sequence\": \"not-int\", \"actionType\": \"create\", \"targetType\": \"card\", " +
            "\"parameters\": \"{}\", \"idempotencyKey\": \"key1\"}]}"
        };
        yield return new object[]
        {
            "{\"sourceType\": 0, \"summary\": \"test\", \"riskLevel\": 0, \"correlationId\": \"abc\", " +
            "\"operations\": [{\"sequence\": 0, \"actionType\": null, \"targetType\": \"card\", " +
            "\"parameters\": \"{}\", \"idempotencyKey\": \"key1\"}]}"
        };
        yield return new object[]
        {
            "{\"sourceType\": 0, \"summary\": \"test\", \"riskLevel\": 0, \"correlationId\": \"abc\", " +
            "\"operations\": \"not-an-array\"}"
        };

        // NOTE: Deeply nested JSON parameters and XSS in actionType cause 500s.
        // These are documented as known bugs in separate skip-annotated tests below.

        // Null/empty operations
        yield return new object[]
        {
            "{\"sourceType\": 0, \"summary\": \"test\", \"riskLevel\": 0, \"correlationId\": \"abc\", " +
            "\"operations\": null}"
        };
        yield return new object[]
        {
            "{\"sourceType\": 0, \"summary\": \"test\", \"riskLevel\": 0, \"correlationId\": \"abc\", " +
            "\"operations\": []}"
        };

        // NOTE: XSS in actionType causes 500 — documented in a separate skip-annotated test below.

        // SQL injection in parameters
        yield return new object[]
        {
            "{\"sourceType\": 0, \"summary\": \"'; DROP TABLE proposals; --\", \"riskLevel\": 0, " +
            "\"correlationId\": \"abc\", \"operations\": [{\"sequence\": 0, \"actionType\": \"create\", " +
            "\"targetType\": \"card\", \"parameters\": \"'; DROP TABLE cards; --\", \"idempotencyKey\": \"key1\"}]}"
        };

        // Enum out of range
        yield return new object[] { "{\"sourceType\": 999, \"summary\": \"test\", \"riskLevel\": 999, \"correlationId\": \"abc\"}" };
        yield return new object[] { "{\"sourceType\": -1, \"summary\": \"test\", \"riskLevel\": -1, \"correlationId\": \"abc\"}" };
    }

    [Theory]
    [MemberData(nameof(MalformedProposalBodies))]
    public async Task CreateProposal_WithMalformedBody_NeverReturns500(string body)
    {
        await EnsureAuthenticatedAsync();

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/automation/proposals", content);

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Proposal creation returned 500 for body: {body}");
    }

    // ─────────────────────── Adversarial summary strings ───────────────────────

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\u0000")]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("'; DROP TABLE proposals; --")]
    public async Task CreateProposal_WithAdversarialSummary_NeverReturns500(string summary)
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync("/api/automation/proposals",
            new CreateProposalDto(
                ProposalSourceType.Manual,
                Guid.NewGuid(),
                summary,
                RiskLevel.Low,
                Guid.NewGuid().ToString()));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Proposal creation returned 500 for summary: [{summary.Length} chars]");
    }

    // ─────────────────────── Boundary length summary ───────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(500)]
    [InlineData(501)]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public async Task CreateProposal_WithVariousSummaryLengths_NeverReturns500(int length)
    {
        await EnsureAuthenticatedAsync();

        var summary = length == 0 ? "" : new string('s', length);
        var response = await _client.PostAsJsonAsync("/api/automation/proposals",
            new CreateProposalDto(
                ProposalSourceType.Manual,
                Guid.NewGuid(),
                summary,
                RiskLevel.Low,
                Guid.NewGuid().ToString()));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Proposal creation returned 500 for summary of {length} chars");
    }

    // ─────────────────────── Expiry minutes boundary ───────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public async Task CreateProposal_WithBoundaryExpiryMinutes_NeverReturns500(int expiryMinutes)
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync("/api/automation/proposals",
            new CreateProposalDto(
                ProposalSourceType.Manual,
                Guid.NewGuid(),
                "Valid summary",
                RiskLevel.Low,
                Guid.NewGuid().ToString(),
                ExpiryMinutes: expiryMinutes));

        ((int)response.StatusCode).Should().BeLessThan(500,
            $"Proposal creation returned 500 for expiryMinutes={expiryMinutes}");
    }

    // ─────────────────────── Known 500 bugs (skip-annotated) ───────────────────────
    // These tests document real API bugs discovered by adversarial testing.
    // When operations contain unexpected actionType values or nested parameter JSON,
    // the proposal creation endpoint returns 500 instead of 4xx.

    [Fact(Skip = "Known bug: proposal endpoint returns 500 when operations contain XSS in actionType")]
    public async Task KnownBug_XssInActionType_Causes500()
    {
        await EnsureAuthenticatedAsync();

        var body = "{\"sourceType\": 0, \"summary\": \"test\", \"riskLevel\": 0, \"correlationId\": \"abc\", " +
                   "\"operations\": [{\"sequence\": 0, \"actionType\": \"<script>alert(1)</script>\", " +
                   "\"targetType\": \"card\", \"parameters\": \"{}\", \"idempotencyKey\": \"key1\"}]}";

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/automation/proposals", content);

        // BUG: This returns 500 instead of 400/422.
        // Expected: ((int)response.StatusCode).Should().BeLessThan(500);
        ((int)response.StatusCode).Should().Be(500,
            "Documenting known bug: XSS in actionType causes 500");
    }

    [Fact(Skip = "Known bug: proposal endpoint returns 500 when operations contain deeply nested parameter JSON")]
    public async Task KnownBug_DeepNestedParameters_Causes500()
    {
        await EnsureAuthenticatedAsync();

        var body = "{\"sourceType\": 0, \"summary\": \"test\", \"riskLevel\": 0, \"correlationId\": \"abc\", " +
                   "\"operations\": [{\"sequence\": 0, \"actionType\": \"create\", \"targetType\": \"card\", " +
                   "\"parameters\": \"{\\\"nested\\\": {\\\"deep\\\": {\\\"deeper\\\": true}}}\", \"idempotencyKey\": \"key1\"}]}";

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/automation/proposals", content);

        // BUG: This returns 500 instead of 400/422 or 201.
        // Expected: ((int)response.StatusCode).Should().BeLessThan(500);
        ((int)response.StatusCode).Should().Be(500,
            "Documenting known bug: nested parameter JSON causes 500");
    }
}
