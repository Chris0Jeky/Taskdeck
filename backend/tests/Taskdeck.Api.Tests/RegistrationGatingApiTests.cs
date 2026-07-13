using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Controllers;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class RegistrationGatingApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public RegistrationGatingApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(RegistrationMode.Open, true, false)]
    [InlineData(RegistrationMode.InviteOnly, true, true)]
    [InlineData(RegistrationMode.Closed, true, true)]
    public async Task Providers_ExposeTruthfulPublicRegistrationAvailability(
        RegistrationMode mode,
        bool isRegistrationAvailable,
        bool inviteRequired)
    {
        using var factory = CreateFactory(mode);
        using var client = factory.CreateClient();

        var providers = await client.GetFromJsonAsync<AuthProvidersResponse>("/api/auth/providers");

        providers.Should().NotBeNull();
        providers!.Registration.Should().Be(new RegistrationAvailabilityResponse(
            mode.ToString(),
            isRegistrationAvailable,
            inviteRequired));
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
    public async Task ClosedMode_RequiresOperatorInviteForFirstUser_ThenClosesWhileLoginStillWorks()
    {
        using var factory = CreateFactory(RegistrationMode.Closed);
        using var client = factory.CreateClient();

        var remoteBootstrap = await RegisterAsync(client, "remote-first");
        await ApiTestHarness.AssertErrorContractAsync(
            remoteBootstrap,
            HttpStatusCode.Forbidden,
            ErrorCodes.Forbidden);

        var bootstrapInvite = await CreateInviteAsync(factory);
        var first = await RegisterAsync(client, "closed-first", bootstrapInvite);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstPayload = await first.Content.ReadFromJsonAsync<AuthResultDto>();
        firstPayload.Should().NotBeNull();
        firstPayload!.User.DefaultRole.Should().Be(UserRole.Owner);

        var providers = await client.GetFromJsonAsync<AuthProvidersResponse>("/api/auth/providers");
        providers.Should().NotBeNull();
        providers!.Registration.Should().Be(new RegistrationAvailabilityResponse(
            RegistrationMode.Closed.ToString(),
            IsRegistrationAvailable: false,
            InviteRequired: false));

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
    public async Task InviteOnlyMode_RequiresInviteForFirstUserAndEveryLaterRegistration()
    {
        using var factory = CreateFactory(RegistrationMode.InviteOnly);
        using var client = factory.CreateClient();

        var withoutInvite = await RegisterAsync(client, "invite-missing");
        await ApiTestHarness.AssertErrorContractAsync(
            withoutInvite,
            HttpStatusCode.Forbidden,
            ErrorCodes.Forbidden);

        var bootstrapInvite = await CreateInviteAsync(factory);
        var bootstrap = await RegisterAsync(client, "invite-bootstrap", bootstrapInvite);
        bootstrap.StatusCode.Should().Be(HttpStatusCode.OK);
        var bootstrapPayload = await bootstrap.Content.ReadFromJsonAsync<AuthResultDto>();
        bootstrapPayload.Should().NotBeNull();
        bootstrapPayload!.User.DefaultRole.Should().Be(UserRole.Owner);

        var inviteCode = await CreateInviteAsync(factory);
        var accepted = await RegisterAsync(client, "invite-accepted", inviteCode);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);

        var reused = await RegisterAsync(client, "invite-reused", inviteCode);
        await ApiTestHarness.AssertErrorContractAsync(
            reused,
            HttpStatusCode.Forbidden,
            ErrorCodes.Forbidden);
    }

    [Theory]
    [InlineData(RegistrationMode.Closed, "Registration is closed by this Taskdeck instance.")]
    [InlineData(RegistrationMode.InviteOnly, "A valid registration invite is required.")]
    public async Task RestrictiveMode_DeniesKnownAndUnknownIdentitiesWithSameContractWithoutInvite(
        RegistrationMode mode,
        string expectedMessage)
    {
        using var factory = CreateFactory(mode);
        using var client = factory.CreateClient();
        var bootstrapInvite = await CreateInviteAsync(factory);
        var bootstrap = await RegisterAsync(client, "known-user", bootstrapInvite);
        bootstrap.StatusCode.Should().Be(HttpStatusCode.OK);

        var known = await RegisterAsync(client, "known-user");
        var unknown = await RegisterAsync(client, "unknown-user");

        var knownError = await AssertForbiddenAndReadErrorAsync(known);
        var unknownError = await AssertForbiddenAndReadErrorAsync(unknown);
        knownError.GetProperty("message").GetString().Should().Be(expectedMessage);
        unknownError.GetProperty("message").GetString().Should().Be(expectedMessage);
    }

    [Fact]
    public async Task InviteOnlyMode_DuplicateIdentityRollsBackInviteConsumption()
    {
        using var factory = CreateFactory(RegistrationMode.InviteOnly);
        using var client = factory.CreateClient();
        var bootstrapInvite = await CreateInviteAsync(factory);
        (await RegisterAsync(client, "existing-user", bootstrapInvite)).StatusCode.Should().Be(HttpStatusCode.OK);
        var inviteCode = await CreateInviteAsync(factory);

        var duplicate = await RegisterAsync(client, "existing-user", inviteCode);
        await ApiTestHarness.AssertErrorContractAsync(duplicate, HttpStatusCode.Conflict, ErrorCodes.Conflict);

        var retry = await RegisterAsync(client, "unique-user", inviteCode);
        retry.StatusCode.Should().Be(HttpStatusCode.OK,
            "the duplicate-user transaction must roll back invite consumption");
    }

    [Fact]
    public async Task InviteOnlyMode_ConcurrentSingleInviteRedemptionCreatesExactlyOneUser()
    {
        using var factory = CreateFactory(RegistrationMode.InviteOnly);
        using var bootstrapClient = factory.CreateClient();
        var bootstrapInvite = await CreateInviteAsync(factory);
        (await RegisterAsync(bootstrapClient, "bootstrap-user", bootstrapInvite)).StatusCode.Should().Be(HttpStatusCode.OK);
        var inviteCode = await CreateInviteAsync(factory);
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();

        var responses = await Task.WhenAll(
            RegisterAsync(firstClient, "invite-race-a", inviteCode),
            RegisterAsync(secondClient, "invite-race-b", inviteCode));

        responses.Count(response => response.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Forbidden).Should().Be(1);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        dbContext.Users.Count().Should().Be(2,
            "only the bootstrap owner and one invite redeemer may be persisted");
        dbContext.ExternalLogins.Count().Should().Be(0,
            "password registration must not leave an orphan external identity");
    }

    [Fact]
    public async Task ClosedMode_RealStoreGatesNewExternalIdentityButKeepsExistingLoginWorking()
    {
        using var factory = CreateFactory(RegistrationMode.Closed);
        var bootstrapInvite = await CreateInviteAsync(factory);
        using var scope = factory.Services.CreateScope();
        var authentication = scope.ServiceProvider.GetRequiredService<AuthenticationService>();
        var owner = new ExternalLoginDto(
            "GitHub",
            "external-owner-id",
            "external-owner",
            "external-owner@example.test",
            InviteCode: bootstrapInvite);

        var firstLogin = await authentication.ExternalLoginAsync(owner);
        var newIdentity = await authentication.ExternalLoginAsync(new ExternalLoginDto(
            "GitHub",
            "remote-new-id",
            "remote-new",
            "remote-new@example.test"));
        var existingLogin = await authentication.ExternalLoginAsync(owner with { InviteCode = null });

        firstLogin.IsSuccess.Should().BeTrue();
        firstLogin.Value.User.DefaultRole.Should().Be(UserRole.Owner);
        newIdentity.IsSuccess.Should().BeFalse();
        newIdentity.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        newIdentity.ErrorMessage.Should().Be(RegistrationPolicyService.RegistrationClosedMessage);
        existingLogin.IsSuccess.Should().BeTrue();
        existingLogin.Value.User.Id.Should().Be(firstLogin.Value.User.Id);

        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        dbContext.Users.Count().Should().Be(1);
        dbContext.ExternalLogins.Count().Should().Be(1);
    }

    [Theory]
    [InlineData(RegistrationMode.Closed)]
    [InlineData(RegistrationMode.InviteOnly)]
    public async Task RestrictiveMode_DisablesAuthenticatedDirectUserCreation(
        RegistrationMode mode)
    {
        using var factory = CreateFactory(mode);
        using var client = factory.CreateClient();
        var bootstrapInvite = await CreateInviteAsync(factory);
        var bootstrap = await RegisterAsync(client, "direct-create-owner", bootstrapInvite);
        bootstrap.StatusCode.Should().Be(HttpStatusCode.OK);
        var bootstrapPayload = await bootstrap.Content.ReadFromJsonAsync<AuthResultDto>();
        bootstrapPayload.Should().NotBeNull();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            bootstrapPayload!.Token);
        var unusedInvite = await CreateInviteAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/users",
            new CreateUserDto(
                "direct-create-bypass",
                "direct-create-bypass@example.test",
                "password123",
                InviteCode: unusedInvite));

        var error = await AssertForbiddenAndReadErrorAsync(response);
        error.GetProperty("message").GetString()
            .Should()
            .Be(UsersController.RestrictedCreationMessage);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        dbContext.Users.Count().Should().Be(1);
        dbContext.RegistrationInvites.Count(invite => invite.ConsumedAt != null).Should().Be(1,
            "the direct endpoint must not consume the unused invite");
    }

    private WebApplicationFactory<Program> CreateFactory(RegistrationMode mode)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:Registration:Mode", mode.ToString());
        });
    }

    private static async Task<string> CreateInviteAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var policy = scope.ServiceProvider.GetRequiredService<IRegistrationPolicyService>();
        var invite = await policy.CreateInviteAsync(TimeSpan.FromDays(1));
        invite.IsSuccess.Should().BeTrue();
        return invite.Value.Code;
    }

    private static async Task<JsonElement> AssertForbiddenAndReadErrorAsync(HttpResponseMessage response)
    {
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Forbidden, ErrorCodes.Forbidden);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
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
