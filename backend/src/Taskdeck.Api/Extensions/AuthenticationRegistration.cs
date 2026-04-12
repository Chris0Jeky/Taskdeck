using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Microsoft.IdentityModel.Tokens;
using Taskdeck.Api.Contracts;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using HeaderNames = Microsoft.Net.Http.Headers.HeaderNames;

namespace Taskdeck.Api.Extensions;

public static class AuthenticationRegistration
{
    public static IServiceCollection AddTaskdeckAuthentication(
        this IServiceCollection services,
        JwtSettings jwtSettings,
        GitHubOAuthSettings? gitHubOAuthSettings = null)
    {
        if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey) ||
            jwtSettings.SecretKey.Length < 32 ||
            string.IsNullOrWhiteSpace(jwtSettings.Issuer) ||
            string.IsNullOrWhiteSpace(jwtSettings.Audience))
        {
            return services;
        }

        var authBuilder = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrWhiteSpace(accessToken) && path.StartsWithSegments("/hubs/boards"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        if (context.Response.HasStarted)
                        {
                            return;
                        }

                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.Headers[HeaderNames.WWWAuthenticate] =
                            BuildWwwAuthenticateHeaderValue(context.Error, context.ErrorDescription);
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new ApiErrorResponse(
                            ErrorCodes.Unauthorized,
                            "Authentication is required to access this resource."));
                    },
                    OnForbidden = async context =>
                    {
                        if (context.Response.HasStarted)
                        {
                            return;
                        }

                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new ApiErrorResponse(
                            ErrorCodes.Forbidden,
                            "You do not have permission to access this resource."));
                    }
                };
            });

        // Environment-gated: only add GitHub OAuth when configured
        if (gitHubOAuthSettings is { IsConfigured: true })
        {
            authBuilder.AddOAuth("GitHub", options =>
            {
                options.ClientId = gitHubOAuthSettings.ClientId;
                options.ClientSecret = gitHubOAuthSettings.ClientSecret;
                options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
                options.TokenEndpoint = "https://github.com/login/oauth/access_token";
                options.UserInformationEndpoint = "https://api.github.com/user";
                options.CallbackPath = "/api/auth/github/oauth-redirect";
                options.SaveTokens = false;

                // PKCE (Proof Key for Code Exchange) — defense-in-depth against
                // authorization code interception attacks. GitHub supports PKCE
                // and ASP.NET Core 8+ handles code_verifier/code_challenge automatically.
                options.UsePkce = true;

                options.Scope.Add("read:user");
                options.Scope.Add("user:email");

                options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
                options.ClaimActions.MapJsonKey(ClaimTypes.Name, "login");
                options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
                options.ClaimActions.MapJsonKey("urn:github:name", "name");
                options.ClaimActions.MapJsonKey("urn:github:login", "login");
                options.ClaimActions.MapJsonKey("urn:github:avatar", "avatar_url");

                // OAuthHandler fetches UserInformationEndpoint automatically
                // and applies ClaimActions — no custom OnCreatingTicket needed.
            });
        }

        return services;
    }

    private static string BuildWwwAuthenticateHeaderValue(string? error, string? errorDescription)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "Bearer";
        }

        var escapedError = EscapeAuthHeaderValue(error);
        if (string.IsNullOrWhiteSpace(errorDescription))
        {
            return $"Bearer error=\"{escapedError}\"";
        }

        return $"Bearer error=\"{escapedError}\", error_description=\"{EscapeAuthHeaderValue(errorDescription)}\"";
    }

    private static string EscapeAuthHeaderValue(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
