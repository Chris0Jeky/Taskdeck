using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.Extensions.Http;
using Taskdeck.Api.Contracts;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using HeaderNames = Microsoft.Net.Http.Headers.HeaderNames;

namespace Taskdeck.Api.Extensions;

public static class AuthenticationRegistration
{
    /// <summary>
    /// Cookie scheme used for temporary external auth state (OAuth/OIDC handshake).
    /// </summary>
    public const string ExternalAuthenticationScheme = "External";

    public static IServiceCollection AddTaskdeckAuthentication(
        this IServiceCollection services,
        JwtSettings jwtSettings,
        GitHubOAuthSettings? gitHubOAuthSettings = null,
        OidcSettings? oidcSettings = null,
        CircuitBreakerStateTracker? circuitBreakerTracker = null,
        CircuitBreakerSettings? circuitBreakerSettings = null)
    {
        if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey) ||
            jwtSettings.SecretKey.Length < 32 ||
            string.IsNullOrWhiteSpace(jwtSettings.Issuer) ||
            string.IsNullOrWhiteSpace(jwtSettings.Audience))
        {
            return services;
        }

        var authBuilder = services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultSignInScheme = ExternalAuthenticationScheme;
            })
            .AddCookie(ExternalAuthenticationScheme, options =>
            {
                options.Cookie.Name = ".Taskdeck.ExternalAuth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                options.SlidingExpiration = false;
            })
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
                options.SignInScheme = ExternalAuthenticationScheme;
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

                // Validate OAuth scopes after token exchange.
                // GitHub returns the granted scopes in the token response body as "scope"
                // (comma-separated). Reject authentication if required scopes are missing.
                var scopeSettings = gitHubOAuthSettings;
                options.Events = new OAuthEvents
                {
                    OnCreatingTicket = context =>
                    {
                        var scopeValidator = context.HttpContext.RequestServices
                            .GetRequiredService<OAuthScopeValidator>();

                        // GitHub returns granted scopes in the token response "scope" field.
                        // The X-OAuth-Scopes header appears on API responses, but the token
                        // response body is the authoritative source at auth time.
                        var grantedScopesRaw = context.TokenResponse?.Response?.RootElement
                            .TryGetProperty("scope", out var scopeElement) == true
                            ? scopeElement.GetString()
                            : null;

                        var validationResult = scopeValidator.Validate(
                            grantedScopesRaw,
                            scopeSettings.RequiredScopes,
                            scopeSettings.ExpectedScopes);

                        if (!validationResult.IsValid)
                        {
                            var logger = context.HttpContext.RequestServices
                                .GetRequiredService<ILoggerFactory>()
                                .CreateLogger("Taskdeck.Api.OAuth.ScopeValidation");

                            logger.LogError(
                                "GitHub OAuth authentication rejected: missing required scopes. " +
                                "Missing: [{MissingScopes}]. User must re-authorize with required permissions.",
                                string.Join(", ", validationResult.MissingRequiredScopes));

                            context.Fail(validationResult.ErrorMessage
                                ?? "GitHub OAuth scope validation failed");
                        }

                        return Task.CompletedTask;
                    }
                };

                // Circuit breaker for the OAuth backchannel (token exchange + user info).
                if (circuitBreakerTracker is not null && circuitBreakerSettings is not null)
                {
                    options.BackchannelHttpHandler = BuildOAuthBackchannelHandler(
                        circuitBreakerTracker, circuitBreakerSettings, "GitHubOAuth");
                }
            });
        }

        // Environment-gated: add OIDC providers when configured
        if (oidcSettings != null)
        {
            foreach (var provider in oidcSettings.ConfiguredProviders)
            {
                var schemeName = $"Oidc_{provider.Name}";
                var callbackPath = !string.IsNullOrWhiteSpace(provider.CallbackPath)
                    ? provider.CallbackPath
                    : $"/api/auth/oidc/{provider.Name.ToLowerInvariant()}/oauth-redirect";

                authBuilder.AddOpenIdConnect(schemeName, provider.DisplayName, options =>
                {
                    options.SignInScheme = ExternalAuthenticationScheme;
                    options.Authority = provider.Authority;
                    options.ClientId = provider.ClientId;
                    options.ClientSecret = provider.ClientSecret;
                    options.CallbackPath = callbackPath;
                    options.ResponseType = "code";
                    options.SaveTokens = false;
                    options.GetClaimsFromUserInfoEndpoint = true;

                    options.Scope.Clear();
                    foreach (var scope in provider.Scopes)
                    {
                        options.Scope.Add(scope);
                    }

                    // Map standard OIDC claims to ClaimTypes
                    options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "sub");
                    options.ClaimActions.MapJsonKey(ClaimTypes.Name, "preferred_username");
                    options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
                    options.ClaimActions.MapJsonKey("name", "name");

                    // Circuit breaker for the OIDC backchannel.
                    if (circuitBreakerTracker is not null && circuitBreakerSettings is not null)
                    {
                        options.BackchannelHttpHandler = BuildOAuthBackchannelHandler(
                            circuitBreakerTracker, circuitBreakerSettings, $"OIDC_{provider.Name}");
                    }
                });
            }
        }

        return services;
    }

    /// <summary>
    /// Creates a delegating handler chain that wraps a default
    /// <see cref="SocketsHttpHandler"/> with a Polly circuit breaker policy.
    /// Used for the OAuth/OIDC backchannel (token exchange and user-info requests).
    /// </summary>
    internal static HttpMessageHandler BuildOAuthBackchannelHandler(
        CircuitBreakerStateTracker tracker,
        CircuitBreakerSettings settings,
        string circuitName)
    {
        var policy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: settings.FailureThreshold,
                durationOfBreak: TimeSpan.FromSeconds(settings.BreakDurationSeconds),
                onBreak: (outcome, breakDuration) =>
                {
                    var reason = outcome.Exception?.Message ?? $"HTTP {(int)(outcome.Result?.StatusCode ?? 0)}";
                    tracker.RecordState(circuitName, CircuitState.Open, reason);
                },
                onReset: () =>
                {
                    tracker.RecordState(circuitName, CircuitState.Closed);
                },
                onHalfOpen: () =>
                {
                    tracker.RecordState(circuitName, CircuitState.HalfOpen);
                });

        return new PolicyHttpMessageHandler(policy)
        {
            InnerHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false
            }
        };
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
