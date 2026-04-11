using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.Filters;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using AuthenticationService = Taskdeck.Application.Services.AuthenticationService;

namespace Taskdeck.Api.Controllers;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword, string? MfaCode = null);
public record ExchangeCodeRequest(string Code);

/// <summary>
/// Authentication endpoints — register, login, change password, and GitHub OAuth flow.
/// All endpoints return a JWT token on successful authentication.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : AuthenticatedControllerBase
{
    private readonly AuthenticationService _authService;
    private readonly GitHubOAuthSettings _gitHubOAuthSettings;
    private readonly OidcSettings _oidcSettings;
    private readonly MfaService _mfaService;

    // Short-lived, single-use authorization codes to avoid exposing JWT in URLs.
    // Key: code, Value: (token, expiry). Codes expire after 60 seconds.
    private static readonly ConcurrentDictionary<string, (AuthResultDto Result, DateTimeOffset Expiry)> _authCodes = new();

    public AuthController(
        AuthenticationService authService,
        GitHubOAuthSettings gitHubOAuthSettings,
        OidcSettings oidcSettings,
        MfaService mfaService,
        IUserContext userContext)
        : base(userContext)
    {
        _authService = authService;
        _gitHubOAuthSettings = gitHubOAuthSettings;
        _oidcSettings = oidcSettings;
        _mfaService = mfaService;
    }

    /// <summary>
    /// Authenticate with username/email and password. Returns a JWT token.
    /// </summary>
    /// <param name="dto">Login credentials.</param>
    /// <returns>JWT token and user profile.</returns>
    /// <response code="200">Login successful — JWT token returned.</response>
    /// <response code="401">Invalid credentials.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpPost("login")]
    [SuppressModelStateValidation]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    [ProducesResponseType(typeof(AuthResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginDto? dto)
    {
        if (dto is null
            || string.IsNullOrWhiteSpace(dto.UsernameOrEmail)
            || string.IsNullOrWhiteSpace(dto.Password))
        {
            return Unauthorized(new ApiErrorResponse(
                ErrorCodes.AuthenticationFailed,
                "Invalid username/email or password"));
        }

        var result = await _authService.LoginAsync(dto!);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Register a new user account. Returns a JWT token.
    /// </summary>
    /// <param name="dto">Registration details: username, email, password.</param>
    /// <returns>JWT token and user profile.</returns>
    /// <response code="200">Registration successful — JWT token returned.</response>
    /// <response code="400">Validation error (e.g., duplicate username/email).</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpPost("register")]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    [ProducesResponseType(typeof(AuthResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Register([FromBody] CreateUserDto dto)
    {
        var result = await _authService.RegisterAsync(dto);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Change the password for the authenticated caller.
    /// The target user is always derived from the JWT — client-supplied user IDs are not accepted.
    /// When MFA is enabled and RequireMfaForSensitiveActions is true, a valid MFA code is required.
    /// </summary>
    /// <param name="request">Current password, new password, and optional MFA code.</param>
    /// <response code="204">Password changed successfully.</response>
    /// <response code="400">Validation error.</response>
    /// <response code="401">Not authenticated or current password is incorrect.</response>
    /// <response code="403">MFA verification required but not provided or invalid.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        // Enforce MFA for sensitive actions when policy requires it
        if (await _mfaService.IsMfaRequiredForSensitiveActionAsync(callerUserId))
        {
            if (string.IsNullOrWhiteSpace(request.MfaCode))
                return StatusCode(StatusCodes.Status403Forbidden, new ApiErrorResponse(
                    ErrorCodes.Forbidden, "MFA verification is required for this action"));

            var mfaResult = await _mfaService.VerifyCodeAsync(callerUserId, request.MfaCode);
            if (!mfaResult.IsSuccess)
                return StatusCode(StatusCodes.Status403Forbidden, new ApiErrorResponse(
                    ErrorCodes.AuthenticationFailed, "Invalid MFA verification code"));
        }

        var result = await _authService.ChangePasswordAsync(callerUserId, request.CurrentPassword, request.NewPassword);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }

    /// <summary>
    /// Initiates GitHub OAuth login flow. Only available when GitHub OAuth is configured.
    /// </summary>
    [HttpGet("github/login")]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    public IActionResult GitHubLogin([FromQuery] string? returnUrl = null)
    {
        if (!_gitHubOAuthSettings.IsConfigured)
            return NotFound(new ApiErrorResponse(ErrorCodes.NotFound, "GitHub OAuth is not configured"));

        // Validate returnUrl to prevent open redirect
        if (!string.IsNullOrWhiteSpace(returnUrl) && !Url.IsLocalUrl(returnUrl))
            return BadRequest(new ApiErrorResponse(ErrorCodes.ValidationError, "Invalid return URL"));

        var properties = new Microsoft.AspNetCore.Authentication.AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(GitHubCallback), new { returnUrl }),
            Items = { { "LoginProvider", "GitHub" } }
        };

        return Challenge(properties, "GitHub");
    }

    /// <summary>
    /// Handles the GitHub OAuth callback, creates/links the user, and redirects with a JWT token.
    /// </summary>
    [HttpGet("github/callback")]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    public async Task<IActionResult> GitHubCallback([FromQuery] string? returnUrl = null)
    {
        if (!_gitHubOAuthSettings.IsConfigured)
            return NotFound(new ApiErrorResponse(ErrorCodes.NotFound, "GitHub OAuth is not configured"));

        var authenticateResult = await HttpContext.AuthenticateAsync("GitHub");
        if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
        {
            return Unauthorized(new ApiErrorResponse(
                ErrorCodes.AuthenticationFailed,
                "GitHub authentication failed"));
        }

        var claims = authenticateResult.Principal.Claims.ToList();
        var providerUserId = claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var username = claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Name)?.Value
                       ?? claims.FirstOrDefault(c => c.Type == "urn:github:login")?.Value;
        var email = claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Email)?.Value;
        var displayName = claims.FirstOrDefault(c => c.Type == "urn:github:name")?.Value;
        var avatarUrl = claims.FirstOrDefault(c => c.Type == "urn:github:avatar")?.Value;

        if (string.IsNullOrWhiteSpace(providerUserId))
        {
            return Unauthorized(new ApiErrorResponse(
                ErrorCodes.AuthenticationFailed,
                "GitHub did not return a user identifier"));
        }

        // GitHub may not return an email if user's email is private
        if (string.IsNullOrWhiteSpace(email))
            email = $"{providerUserId}@users.noreply.github.com";

        if (string.IsNullOrWhiteSpace(username))
            username = $"github-user-{providerUserId}";

        var dto = new ExternalLoginDto(
            Provider: "GitHub",
            ProviderUserId: providerUserId,
            Username: username,
            Email: email,
            DisplayName: displayName,
            AvatarUrl: avatarUrl);

        var result = await _authService.ExternalLoginAsync(dto);

        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        // Sign out the temporary cookie used during the OAuth handshake
        await HttpContext.SignOutAsync(AuthenticationRegistration.ExternalAuthenticationScheme);

        // Security: Do NOT put the JWT in the URL. Use a short-lived, single-use
        // authorization code that the frontend exchanges via POST.
        var code = GenerateAuthCode();
        _authCodes[code] = (result.Value, DateTimeOffset.UtcNow.AddSeconds(60));
        CleanupExpiredCodes();

        var safeReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : "/";

        var separator = safeReturnUrl.Contains('?') ? "&" : "?";
        return Redirect($"{safeReturnUrl}{separator}oauth_code={Uri.EscapeDataString(code)}");
    }

    /// <summary>
    /// Exchanges a short-lived OAuth authorization code for a JWT token.
    /// The code is single-use and expires after 60 seconds.
    /// </summary>
    [HttpPost("github/exchange")]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    public IActionResult ExchangeCode([FromBody] ExchangeCodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new ApiErrorResponse(ErrorCodes.ValidationError, "Code is required"));

        if (!_authCodes.TryRemove(request.Code, out var entry))
            return Unauthorized(new ApiErrorResponse(ErrorCodes.AuthenticationFailed, "Invalid or expired code"));

        if (DateTimeOffset.UtcNow > entry.Expiry)
            return Unauthorized(new ApiErrorResponse(ErrorCodes.AuthenticationFailed, "Code has expired"));

        return Ok(entry.Result);
    }

    /// <summary>
    /// Returns available authentication providers on this instance.
    /// </summary>
    [HttpGet("providers")]
    public IActionResult GetProviders()
    {
        var oidcProviders = _oidcSettings.ConfiguredProviders
            .Select(p => new OidcProviderInfoDto(p.Name, p.DisplayName))
            .ToList();

        return Ok(new
        {
            GitHub = _gitHubOAuthSettings.IsConfigured,
            Oidc = oidcProviders
        });
    }

    /// <summary>
    /// Initiates OIDC login flow for a named provider. Only available when the provider is configured.
    /// </summary>
    [HttpGet("oidc/{providerName}/login")]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    public IActionResult OidcLogin(string providerName, [FromQuery] string? returnUrl = null)
    {
        var provider = _oidcSettings.ConfiguredProviders
            .FirstOrDefault(p => string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));

        if (provider == null)
            return NotFound(new ApiErrorResponse(ErrorCodes.NotFound, $"OIDC provider '{providerName}' is not configured"));

        if (!string.IsNullOrWhiteSpace(returnUrl) && !Url.IsLocalUrl(returnUrl))
            return BadRequest(new ApiErrorResponse(ErrorCodes.ValidationError, "Invalid return URL"));

        var schemeName = $"Oidc_{provider.Name}";
        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(OidcCallback), new { providerName = provider.Name, returnUrl }),
            Items = { { "LoginProvider", provider.Name } }
        };

        return Challenge(properties, schemeName);
    }

    /// <summary>
    /// Handles the OIDC callback, creates/links the user, and redirects with a short-lived code.
    /// </summary>
    [HttpGet("oidc/{providerName}/callback")]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    public async Task<IActionResult> OidcCallback(string providerName, [FromQuery] string? returnUrl = null)
    {
        var provider = _oidcSettings.ConfiguredProviders
            .FirstOrDefault(p => string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));

        if (provider == null)
            return NotFound(new ApiErrorResponse(ErrorCodes.NotFound, $"OIDC provider '{providerName}' is not configured"));

        var schemeName = $"Oidc_{provider.Name}";
        var authenticateResult = await HttpContext.AuthenticateAsync(schemeName);
        if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
        {
            return Unauthorized(new ApiErrorResponse(
                ErrorCodes.AuthenticationFailed,
                $"OIDC authentication with '{provider.DisplayName}' failed"));
        }

        var claims = authenticateResult.Principal.Claims.ToList();
        var providerUserId = claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var username = claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Name)?.Value
                       ?? claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value;
        var email = claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Email)?.Value;
        var displayName = claims.FirstOrDefault(c => c.Type == "name")?.Value;

        if (string.IsNullOrWhiteSpace(providerUserId))
        {
            return Unauthorized(new ApiErrorResponse(
                ErrorCodes.AuthenticationFailed,
                $"OIDC provider '{provider.DisplayName}' did not return a user identifier"));
        }

        if (string.IsNullOrWhiteSpace(email))
            email = $"{provider.Name.ToLowerInvariant()}-{providerUserId}@external.taskdeck.local";

        if (string.IsNullOrWhiteSpace(username))
            username = $"{provider.Name.ToLowerInvariant()}-user-{providerUserId}";

        var dto = new ExternalLoginDto(
            Provider: $"oidc_{provider.Name}",
            ProviderUserId: providerUserId,
            Username: username,
            Email: email,
            DisplayName: displayName,
            AvatarUrl: null);

        var result = await _authService.ExternalLoginAsync(dto);

        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        // Sign out the temporary cookie used during the OIDC handshake
        await HttpContext.SignOutAsync(AuthenticationRegistration.ExternalAuthenticationScheme);

        var code = GenerateAuthCode();
        _authCodes[code] = (result.Value, DateTimeOffset.UtcNow.AddSeconds(60));
        CleanupExpiredCodes();

        var safeReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : "/";

        var separator = safeReturnUrl.Contains('?') ? "&" : "?";
        return Redirect($"{safeReturnUrl}{separator}oauth_code={Uri.EscapeDataString(code)}&oauth_provider=oidc");
    }

    /// <summary>
    /// Exchanges a short-lived OIDC authorization code for a JWT token.
    /// Reuses the same code store as GitHub OAuth.
    /// </summary>
    [HttpPost("oidc/exchange")]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    public IActionResult OidcExchangeCode([FromBody] ExchangeCodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new ApiErrorResponse(ErrorCodes.ValidationError, "Code is required"));

        if (!_authCodes.TryRemove(request.Code, out var entry))
            return Unauthorized(new ApiErrorResponse(ErrorCodes.AuthenticationFailed, "Invalid or expired code"));

        if (DateTimeOffset.UtcNow > entry.Expiry)
            return Unauthorized(new ApiErrorResponse(ErrorCodes.AuthenticationFailed, "Code has expired"));

        return Ok(entry.Result);
    }

    private static string GenerateAuthCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static void CleanupExpiredCodes()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _authCodes)
        {
            if (now > kvp.Value.Expiry)
                _authCodes.TryRemove(kvp.Key, out _);
        }
    }
}
