using System.Security.Cryptography;
using System.Text.Json;
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
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using AuthenticationService = Taskdeck.Application.Services.AuthenticationService;

namespace Taskdeck.Api.Controllers;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record ExchangeCodeRequest(string Code);
public record LinkExchangeRequest(string Code);

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
    private readonly IUnitOfWork _unitOfWork;

    public AuthController(AuthenticationService authService, GitHubOAuthSettings gitHubOAuthSettings, IUserContext userContext, IUnitOfWork unitOfWork)
        : base(userContext)
    {
        _authService = authService;
        _gitHubOAuthSettings = gitHubOAuthSettings;
        _unitOfWork = unitOfWork;
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
    /// </summary>
    /// <param name="request">Current and new password.</param>
    /// <response code="204">Password changed successfully.</response>
    /// <response code="400">Validation error.</response>
    /// <response code="401">Not authenticated or current password is incorrect.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var result = await _authService.ChangePasswordAsync(callerUserId, request.CurrentPassword, request.NewPassword);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }

    /// <summary>
    /// Initiates GitHub OAuth login or account-linking flow. Only available when GitHub OAuth is configured.
    /// The flow is determined entirely from server-side state: if the caller is already authenticated
    /// (carries a valid JWT), this starts an account-linking flow bound to their identity; otherwise
    /// it starts a normal login flow. The client must NOT supply a mode parameter -- the server
    /// derives the intent from authentication state to prevent user-controlled bypass.
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

        // Derive flow from server-side authentication state only.
        // Never use a user-supplied "mode" query parameter -- that would allow an attacker
        // to bypass or force the sensitive account-linking branch (CWE-807 / CodeQL
        // "user-controlled bypass of sensitive method").
        // If the caller is already authenticated (valid JWT), treat this as a link request.
        if (TryGetCurrentUserId(out var callerUserId, out _))
        {
            properties.Items["mode"] = "link";
            properties.Items["link_user_id"] = callerUserId.ToString();
        }

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

        // Sign out the temporary cookie used during the OAuth handshake
        await HttpContext.SignOutAsync("GitHub");

        // Determine if this is a link flow from the tamper-proof OAuth state ONLY.
        // Never trust the query string for mode detection -- attacker could append ?mode=link.
        var isLinkMode = false;
        Guid linkUserId = Guid.Empty;
        if (authenticateResult.Properties?.Items.TryGetValue("mode", out var stateMode) == true
            && stateMode == "link")
        {
            isLinkMode = true;
            if (authenticateResult.Properties.Items.TryGetValue("link_user_id", out var linkUserIdStr)
                && Guid.TryParse(linkUserIdStr, out var parsedLinkUserId))
            {
                linkUserId = parsedLinkUserId;
            }
        }

        // Account linking flow: store the GitHub identity as a link code bound to the user
        if (isLinkMode)
        {
            if (linkUserId == Guid.Empty)
            {
                return BadRequest(new ApiErrorResponse(ErrorCodes.ValidationError,
                    "Account linking requires an authenticated session"));
            }

            var linkCode = GenerateAuthCode();
            var providerData = JsonSerializer.Serialize(new
            {
                provider = "GitHub",
                providerUserId,
                displayName,
                avatarUrl
            });

            var linkAuthCode = OAuthAuthCode.CreateForLinking(
                code: linkCode,
                initiatingUserId: linkUserId,
                providerData: providerData,
                expiresAt: DateTimeOffset.UtcNow.AddSeconds(60));

            await _unitOfWork.OAuthAuthCodes.AddAsync(linkAuthCode);
            await _unitOfWork.SaveChangesAsync();

            var linkReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? returnUrl
                : "/";

            var linkSeparator = linkReturnUrl.Contains('?') ? "&" : "?";
            return Redirect($"{linkReturnUrl}{linkSeparator}oauth_link_code={Uri.EscapeDataString(linkCode)}");
        }

        // Normal login flow
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

        // Store only the user ID in the auth code -- JWT is re-issued at exchange time.
        // This avoids storing plaintext JWTs in the database.
        var code = GenerateAuthCode();
        var authCode = new OAuthAuthCode(
            code: code,
            userId: result.Value.User.Id,
            token: "placeholder", // Not stored; JWT re-issued at exchange
            expiresAt: DateTimeOffset.UtcNow.AddSeconds(60));

        await _unitOfWork.OAuthAuthCodes.AddAsync(authCode);
        await _unitOfWork.SaveChangesAsync();

        // Best-effort cleanup of expired/consumed codes (runs in the same request scope)
        await CleanupExpiredCodesAsync();

        var safeReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : "/";

        var separator = safeReturnUrl.Contains('?') ? "&" : "?";
        return Redirect($"{safeReturnUrl}{separator}oauth_code={Uri.EscapeDataString(code)}");
    }

    /// <summary>
    /// Exchanges a short-lived OAuth authorization code for a JWT token.
    /// The code is single-use and expires after 60 seconds.
    /// JWT is re-issued fresh at exchange time -- never stored in the database.
    /// </summary>
    [HttpPost("github/exchange")]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    public async Task<IActionResult> ExchangeCode([FromBody] ExchangeCodeRequest request)
    {
        // Use a single generic error message for all failure modes to prevent
        // attackers from enumerating codes or determining their state.
        const string genericError = "Invalid or expired code";

        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new ApiErrorResponse(ErrorCodes.ValidationError, "Code is required"));

        // Read the code to check purpose (pre-filter before atomic consume)
        var authCode = await _unitOfWork.OAuthAuthCodes.GetByCodeAsync(request.Code);
        if (authCode == null || authCode.IsLinkingCode || authCode.IsExpired || authCode.IsConsumed)
            return Unauthorized(new ApiErrorResponse(ErrorCodes.AuthenticationFailed, genericError));

        // Atomically consume the code — prevents race conditions with concurrent requests.
        // The SQL also enforces expiry check to close the TOCTOU window.
        var consumed = await _unitOfWork.OAuthAuthCodes.TryConsumeAtomicAsync(request.Code);
        if (!consumed)
            return Unauthorized(new ApiErrorResponse(ErrorCodes.AuthenticationFailed, genericError));

        // Look up the user and re-issue a fresh JWT (never stored in DB)
        var user = await _unitOfWork.Users.GetByIdAsync(authCode.UserId);
        if (user == null)
            return Unauthorized(new ApiErrorResponse(ErrorCodes.AuthenticationFailed, genericError));

        var userDto = new UserDto(
            user.Id,
            user.Username,
            user.Email,
            user.DefaultRole,
            user.IsActive,
            user.CreatedAt,
            user.UpdatedAt);

        // Re-issue JWT at exchange time instead of reading stored token
        var freshToken = _authService.GenerateJwtToken(user);

        return Ok(new AuthResultDto(freshToken, userDto));
    }

    /// <summary>
    /// Exchanges a link code and associates the GitHub account with the authenticated user.
    /// Requires a valid JWT session. The link code must have been initiated by the same user
    /// (CSRF protection: code is bound to the initiating user's identity).
    /// </summary>
    [HttpPost("github/link")]
    [Authorize]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    [ProducesResponseType(typeof(LinkedAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LinkGitHub([FromBody] LinkExchangeRequest request)
    {
        const string genericError = "Invalid or expired link code";

        if (!_gitHubOAuthSettings.IsConfigured)
            return NotFound(new ApiErrorResponse(ErrorCodes.NotFound, "GitHub OAuth is not configured"));

        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new ApiErrorResponse(ErrorCodes.ValidationError, "Link code is required"));

        // Look up and validate the link code with uniform error messages
        var authCode = await _unitOfWork.OAuthAuthCodes.GetByCodeAsync(request.Code);
        if (authCode == null || !authCode.IsLinkingCode || authCode.IsExpired || authCode.IsConsumed)
            return Unauthorized(new ApiErrorResponse(ErrorCodes.AuthenticationFailed, genericError));

        // CSRF protection: verify the link code was initiated by the same user who is
        // exchanging it. This prevents an attacker from generating a link code and
        // tricking a victim into exchanging it.
        if (authCode.UserId != callerUserId)
            return Unauthorized(new ApiErrorResponse(ErrorCodes.AuthenticationFailed, genericError));

        // Atomically consume the code (also checks expiry in SQL to close TOCTOU window)
        var consumed = await _unitOfWork.OAuthAuthCodes.TryConsumeAtomicAsync(request.Code);
        if (!consumed)
            return Unauthorized(new ApiErrorResponse(ErrorCodes.AuthenticationFailed, genericError));

        // Parse the provider data from the link code.
        // Wrap in try-catch to prevent 500 errors from malformed JSON (should not happen in normal
        // operation, but defensive coding for any unexpected data corruption).
        if (string.IsNullOrWhiteSpace(authCode.ProviderData))
            return BadRequest(new ApiErrorResponse(ErrorCodes.ValidationError, "Link code contains no provider data"));

        string? provider;
        string? providerUserId;
        string? displayName;
        string? avatarUrl;
        try
        {
            var providerInfo = JsonSerializer.Deserialize<JsonElement>(authCode.ProviderData);
            provider = providerInfo.GetProperty("provider").GetString() ?? "GitHub";
            providerUserId = providerInfo.GetProperty("providerUserId").GetString();
            displayName = providerInfo.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
            avatarUrl = providerInfo.TryGetProperty("avatarUrl", out var av) ? av.GetString() : null;
        }
        catch (JsonException)
        {
            return BadRequest(new ApiErrorResponse(ErrorCodes.ValidationError, "Link code contains invalid provider data"));
        }
        catch (KeyNotFoundException)
        {
            return BadRequest(new ApiErrorResponse(ErrorCodes.ValidationError, "Link code is missing required provider fields"));
        }

        if (string.IsNullOrWhiteSpace(providerUserId))
            return BadRequest(new ApiErrorResponse(ErrorCodes.ValidationError, "Provider user ID is missing from link code"));

        var result = await _authService.CompleteAccountLinkAsync(callerUserId, provider, providerUserId, displayName, avatarUrl);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Unlinks a GitHub account from the authenticated user.
    /// </summary>
    [HttpDelete("github/link")]
    [Authorize]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlinkGitHub()
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var result = await _authService.UnlinkExternalLoginAsync(callerUserId, "GitHub");
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }

    /// <summary>
    /// Returns the external logins linked to the authenticated user.
    /// </summary>
    [HttpGet("linked-accounts")]
    [Authorize]
    [ProducesResponseType(typeof(IEnumerable<LinkedAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetLinkedAccounts()
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var logins = await _unitOfWork.ExternalLogins.GetByUserIdAsync(callerUserId);
        var dtos = logins.Select(l => new LinkedAccountDto(
            l.Provider,
            l.ProviderUserId,
            l.ProviderDisplayName,
            l.AvatarUrl,
            l.CreatedAt));

        return Ok(dtos);
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

    private static string GenerateAuthCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private async Task CleanupExpiredCodesAsync()
    {
        try
        {
            await _unitOfWork.OAuthAuthCodes.DeleteExpiredAsync(DateTimeOffset.UtcNow);
        }
        catch
        {
            // Cleanup failure is non-critical
        }
    }
}
