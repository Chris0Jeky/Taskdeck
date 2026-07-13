using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class RegistrationGatingApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public RegistrationGatingApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OpenMode_AllowsMultipleRegistrations()
    {
        using var factory = CreateFactory(RegistrationMode.Open);
        using var client = factory.CreateClient();

        var first = await RegisterAsync(client, "open-first");
        var second = await RegisterAsync(client, "open-second");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ClosedMode_AllowsFirstUserThenReturnsStableForbidden_WhileLoginStillWorks()
    {
        using var factory = CreateFactory(RegistrationMode.Closed);
        using var client = factory.CreateClient();

        var first = await RegisterAsync(client, "closed-first");
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await RegisterAsync(client, "closed-second");
        await ApiTestHarness.AssertErrorContractAsync(second, HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        var error = await second.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("message").GetString()
            .Should()
            .Be(RegistrationPolicyService.RegistrationClosedMessage);

        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginDto("closed-first", "password123"));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InviteOnlyMode_AllowsFirstUserAndOneInviteRedemptionOnly()
    {
        using var factory = CreateFactory(RegistrationMode.InviteOnly);
        using var client = factory.CreateClient();

        var bootstrap = await RegisterAsync(client, "invite-bootstrap");
        bootstrap.StatusCode.Should().Be(HttpStatusCode.OK);

        var withoutInvite = await RegisterAsync(client, "invite-missing");
        await ApiTestHarness.AssertErrorContractAsync(
            withoutInvite,
            HttpStatusCode.Forbidden,
            ErrorCodes.Forbidden);

        string inviteCode;
        using (var scope = factory.Services.CreateScope())
        {
            var policy = scope.ServiceProvider.GetRequiredService<IRegistrationPolicyService>();
            var invite = await policy.CreateInviteAsync(TimeSpan.FromDays(1));
            invite.IsSuccess.Should().BeTrue();
            inviteCode = invite.Value.Code;
        }

        var accepted = await RegisterAsync(client, "invite-accepted", inviteCode);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);

        var reused = await RegisterAsync(client, "invite-reused", inviteCode);
        await ApiTestHarness.AssertErrorContractAsync(
            reused,
            HttpStatusCode.Forbidden,
            ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task ClosedMode_ConcurrentFirstRegistrations_AllowExactlyOneBootstrap()
    {
        using var factory = CreateFactory(RegistrationMode.Closed);
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();

        var responses = await Task.WhenAll(
            RegisterAsync(firstClient, "closed-race-a"),
            RegisterAsync(secondClient, "closed-race-b"));

        responses.Count(response => response.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Forbidden).Should().Be(1);
    }

    private WebApplicationFactory<Program> CreateFactory(RegistrationMode mode)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:Registration:Mode", mode.ToString());
        });
    }

    private static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client,
        string username,
        string? inviteCode = null)
    {
        return client.PostAsJsonAsync(
            "/api/auth/register",
            new CreateUserDto(
                username,
                $"{username}@example.test",
                "password123",
                InviteCode: inviteCode));
    }
}
