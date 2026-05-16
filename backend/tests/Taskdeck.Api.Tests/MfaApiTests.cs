using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class MfaApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MfaApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MfaEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        await ApiTestHarness.AssertUnauthorizedAsync(
            await client.GetAsync("/api/auth/mfa/status"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await client.PostAsync("/api/auth/mfa/setup", null));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await client.PostAsJsonAsync("/api/auth/mfa/confirm", new MfaVerifyRequest("000000")));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await client.PostAsJsonAsync("/api/auth/mfa/verify", new MfaVerifyRequest("000000")));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await client.PostAsJsonAsync("/api/auth/mfa/disable", new MfaVerifyRequest("000000")));
    }

    [Fact]
    public async Task GetStatus_ShouldReturnOk_ForAuthenticatedUser()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "mfa-status");

        var response = await client.GetAsync("/api/auth/mfa/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<MfaStatusDto>();
        status.Should().NotBeNull();
        status!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatus_ShouldShowSetupNotAvailable_WhenMfaDisabled()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "mfa-status-disabled");

        var response = await client.GetAsync("/api/auth/mfa/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<MfaStatusDto>();
        status.Should().NotBeNull();
        status!.IsSetupAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Setup_ShouldReturnForbidden_WhenMfaSetupDisabled()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "mfa-setup-disabled");

        var response = await client.PostAsync("/api/auth/mfa/setup", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Forbidden, "Forbidden");
    }

    [Fact]
    public async Task Verify_ShouldReturnError_WhenMfaNotEnabled()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "mfa-verify-not-enabled");

        var response = await client.PostAsJsonAsync("/api/auth/mfa/verify", new MfaVerifyRequest("000000"));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Disable_ShouldReturnError_WhenMfaNotEnabled()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "mfa-disable-not-enabled");

        var response = await client.PostAsJsonAsync("/api/auth/mfa/disable", new MfaVerifyRequest("000000"));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Confirm_ShouldReturnError_WhenNoSetupInProgress()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "mfa-confirm-no-setup");

        var response = await client.PostAsJsonAsync("/api/auth/mfa/confirm", new MfaVerifyRequest("000000"));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }
}

public class MfaApiWithSetupEnabledTests : IClassFixture<MfaEnabledWebApplicationFactory>
{
    private readonly MfaEnabledWebApplicationFactory _factory;

    public MfaApiWithSetupEnabledTests(MfaEnabledWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Setup_ShouldReturnOk_WithSecretAndRecoveryCodes()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "mfa-setup-enabled");

        var response = await client.PostAsync("/api/auth/mfa/setup", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var setup = await response.Content.ReadFromJsonAsync<MfaSetupDto>();
        setup.Should().NotBeNull();
        setup!.SharedSecret.Should().NotBeNullOrWhiteSpace();
        setup.QrCodeUri.Should().NotBeNullOrWhiteSpace();
        setup.RecoveryCodes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetStatus_ShouldShowSetupAvailable_WhenMfaEnabled()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "mfa-status-enabled");

        var response = await client.GetAsync("/api/auth/mfa/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<MfaStatusDto>();
        status.Should().NotBeNull();
        status!.IsSetupAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Confirm_ShouldReturnError_WithInvalidCode()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "mfa-confirm-invalid");

        await client.PostAsync("/api/auth/mfa/setup", null);

        var response = await client.PostAsJsonAsync("/api/auth/mfa/confirm", new MfaVerifyRequest("000000"));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
    }
}
