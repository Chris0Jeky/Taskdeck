using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.Filters;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using AuthenticationService = Taskdeck.Application.Services.AuthenticationService;

namespace Taskdeck.Api.Controllers;

public record ChangePasswordRequest(Guid UserId, string CurrentPassword, string NewPassword);

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthenticationService _authService;
    private readonly GitHubOAuthSettings _gitHubOAuthSettings;

    public AuthController(AuthenticationService authService, GitHubOAuthSettings gitHubOAuthSettings)
    {
        _authService = authService;
        _gitHubOAuthSettings = gitHubOAuthSettings;
    }

    [HttpPost("login")]
    [SuppressModelStateValidation]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
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

    [HttpPost("register")]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    public async Task<IActionResult> Register([FromBody] CreateUserDto dto)
    {
        var result = await _authService.RegisterAsync(dto);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost("change-password")]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var result = await _authService.ChangePasswordAsync(request.UserId, request.CurrentPassword, request.NewPassword);
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
        if (!string.IsNullOrWhiteSpace(returnUrl) && !IsLocalUrl(returnUrl))
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
        await HttpContext.SignOutAsync("GitHub");

        // Redirect to frontend with the JWT token
        var safeReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && IsLocalUrl(returnUrl)
            ? returnUrl
            : "/";

        var separator = safeReturnUrl.Contains('?') ? "&" : "?";
        return Redirect($"{safeReturnUrl}{separator}token={result.Value.Token}");
    }

    /// <summary>
    /// Returns whether GitHub OAuth login is available on this instance.
    /// </summary>
    [HttpGet("providers")]
    public IActionResult GetProviders()
    {
        return Ok(new
        {
            GitHub = _gitHubOAuthSettings.IsConfigured
        });
    }

    private bool IsLocalUrl(string url)
    {
        // Only allow relative URLs (starts with / but not //)
        return !string.IsNullOrWhiteSpace(url)
               && url.StartsWith('/')
               && !url.StartsWith("//")
               && !url.StartsWith("/\\");
    }
}
