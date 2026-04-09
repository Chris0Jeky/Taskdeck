using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// MFA setup, verification, and status endpoints.
/// All endpoints require authentication. MFA is optional and config-gated.
/// </summary>
[ApiController]
[Route("api/auth/mfa")]
[Authorize]
[Produces("application/json")]
public class MfaController : AuthenticatedControllerBase
{
    private readonly MfaService _mfaService;

    public MfaController(MfaService mfaService, IUserContext userContext)
        : base(userContext)
    {
        _mfaService = mfaService;
    }

    /// <summary>
    /// Returns the current MFA status for the authenticated user.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(MfaStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetStatus()
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _mfaService.GetStatusAsync(userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Initiates MFA setup. Returns the shared secret, QR code URI, and recovery codes.
    /// The user must confirm setup by entering a valid TOTP code.
    /// </summary>
    [HttpPost("setup")]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    [ProducesResponseType(typeof(MfaSetupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Setup()
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _mfaService.SetupAsync(userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Confirms MFA setup by validating a TOTP code from the user's authenticator app.
    /// </summary>
    [HttpPost("confirm")]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ConfirmSetup([FromBody] MfaVerifyRequest request)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _mfaService.ConfirmSetupAsync(userId, request.Code);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }

    /// <summary>
    /// Verifies a TOTP code for a sensitive action gate.
    /// </summary>
    [HttpPost("verify")]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Verify([FromBody] MfaVerifyRequest request)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _mfaService.VerifyCodeAsync(userId, request.Code);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }

    /// <summary>
    /// Disables MFA for the authenticated user. Requires a valid TOTP code.
    /// </summary>
    [HttpPost("disable")]
    [EnableRateLimiting(RateLimitingPolicyNames.AuthPerIp)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Disable([FromBody] MfaVerifyRequest request)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _mfaService.DisableAsync(userId, request.Code);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }
}
