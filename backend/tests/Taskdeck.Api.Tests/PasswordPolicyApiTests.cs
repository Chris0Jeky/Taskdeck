using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// HTTP contract for the server-side password policy (#3402/#3419):
/// weak and overlong passwords are rejected with 400, never accepted or 500.
/// </summary>
public class PasswordPolicyApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PasswordPolicyApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("x")]
    [InlineData("12345")]
    public async Task Register_WeakPassword_Returns400(string password)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Username = $"pwdpol-{Guid.NewGuid():N}",
            Email = $"pwdpol-{Guid.NewGuid():N}@example.com",
            Password = password
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_OverlongPassword_Returns400Not500()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Username = $"pwdpol-{Guid.NewGuid():N}",
            Email = $"pwdpol-{Guid.NewGuid():N}@example.com",
            Password = new string('x', 100)
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangePassword_WeakNewPassword_Returns400()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "pwdpol-chpwd");

        var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            CurrentPassword = "password123",
            NewPassword = "x"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
