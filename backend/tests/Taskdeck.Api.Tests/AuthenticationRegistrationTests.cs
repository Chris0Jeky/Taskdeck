using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Api.Tests;

public class AuthenticationRegistrationTests
{
    [Fact]
    public async Task AddTaskdeckAuthentication_ConfiguresExternalSignInScheme_ForRemoteProviders()
    {
        var services = new ServiceCollection();
        var jwtSettings = new JwtSettings
        {
            SecretKey = "TaskdeckTestsOnlySecretKeyMustBeLongEnough123!",
            Issuer = "TaskdeckTests",
            Audience = "TaskdeckUsers",
            ExpirationMinutes = 60
        };
        var gitHubSettings = new GitHubOAuthSettings
        {
            ClientId = "github-client",
            ClientSecret = "github-secret"
        };
        var oidcSettings = new OidcSettings
        {
            Providers =
            [
                new OidcProviderConfig
                {
                    Name = "entra",
                    DisplayName = "Microsoft Entra ID",
                    Authority = "https://login.microsoftonline.com/tenant/v2.0",
                    ClientId = "oidc-client",
                    ClientSecret = "oidc-secret"
                }
            ]
        };

        services.AddLogging();
        services.AddOptions();
        services.AddTaskdeckAuthentication(jwtSettings, gitHubSettings, oidcSettings);

        await using var serviceProvider = services.BuildServiceProvider();

        var authenticationOptions = serviceProvider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        authenticationOptions.DefaultAuthenticateScheme.Should().Be(JwtBearerDefaults.AuthenticationScheme);
        authenticationOptions.DefaultChallengeScheme.Should().Be(JwtBearerDefaults.AuthenticationScheme);
        authenticationOptions.DefaultSignInScheme.Should().Be(AuthenticationRegistration.ExternalAuthenticationScheme);

        var schemeProvider = serviceProvider.GetRequiredService<IAuthenticationSchemeProvider>();
        (await schemeProvider.GetSchemeAsync(AuthenticationRegistration.ExternalAuthenticationScheme)).Should().NotBeNull();
        (await schemeProvider.GetSchemeAsync("GitHub")).Should().NotBeNull();
        (await schemeProvider.GetSchemeAsync("Oidc_entra")).Should().NotBeNull();

        var gitHubOptions = serviceProvider.GetRequiredService<IOptionsMonitor<OAuthOptions>>().Get("GitHub");
        gitHubOptions.SignInScheme.Should().Be(AuthenticationRegistration.ExternalAuthenticationScheme);

        var oidcOptions = serviceProvider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("Oidc_entra");
        oidcOptions.SignInScheme.Should().Be(AuthenticationRegistration.ExternalAuthenticationScheme);
    }
}
