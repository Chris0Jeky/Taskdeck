using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Api.Tests.Extensions;

/// <summary>
/// Guards the fail-fast contract of <see cref="AuthenticationRegistration.AddTaskdeckAuthentication"/>:
/// a missing/short JWT secret must throw at registration rather than silently no-op (which would
/// boot the app with authentication effectively disabled). See issue #1132.
/// </summary>
public class AuthenticationRegistrationTests
{
    private static JwtSettings ValidSettings() => new()
    {
        SecretKey = new string('k', JwtSettings.MinSecretKeyLength),
        Issuer = "Taskdeck",
        Audience = "TaskdeckUsers"
    };

    [Fact]
    public void AddTaskdeckAuthentication_Throws_WhenSecretKeyOneCharBelowFloor()
    {
        var services = new ServiceCollection();
        var settings = ValidSettings();
        settings.SecretKey = new string('k', JwtSettings.MinSecretKeyLength - 1);

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddTaskdeckAuthentication(settings));

        Assert.Contains("misconfigured", ex.Message);
    }

    [Fact]
    public void AddTaskdeckAuthentication_Throws_WhenSecretKeyEmpty()
    {
        var services = new ServiceCollection();
        var settings = ValidSettings();
        settings.SecretKey = "";

        Assert.Throws<InvalidOperationException>(() => services.AddTaskdeckAuthentication(settings));
    }

    [Fact]
    public void AddTaskdeckAuthentication_Throws_WhenIssuerMissing()
    {
        var services = new ServiceCollection();
        var settings = ValidSettings();
        settings.Issuer = "";

        Assert.Throws<InvalidOperationException>(() => services.AddTaskdeckAuthentication(settings));
    }

    [Fact]
    public void AddTaskdeckAuthentication_Throws_WhenServicesNull()
    {
        IServiceCollection services = null!;

        Assert.Throws<ArgumentNullException>(
            () => services.AddTaskdeckAuthentication(ValidSettings()));
    }

    [Fact]
    public void AddTaskdeckAuthentication_Throws_WhenJwtSettingsNull()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(
            () => services.AddTaskdeckAuthentication(null!));
    }

    [Fact]
    public void AddTaskdeckAuthentication_RegistersAuthentication_WhenValid()
    {
        var services = new ServiceCollection();

        var result = services.AddTaskdeckAuthentication(ValidSettings());

        Assert.Same(services, result);
        Assert.Contains(services, d => d.ServiceType == typeof(IAuthenticationSchemeProvider));
    }
}
