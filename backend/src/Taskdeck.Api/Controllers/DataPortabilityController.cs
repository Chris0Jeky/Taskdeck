using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Endpoints for GDPR-style data portability: user data export and account deletion.
/// All endpoints require authentication and scope access strictly to the requesting user.
/// </summary>
[ApiController]
[Authorize]
[Route("api/account")]
public class DataPortabilityController : AuthenticatedControllerBase
{
    private readonly IDataExportService _dataExportService;
    private readonly IAccountDeletionService _accountDeletionService;

    public DataPortabilityController(
        IDataExportService dataExportService,
        IAccountDeletionService accountDeletionService,
        IUserContext userContext)
        : base(userContext)
    {
        _dataExportService = dataExportService;
        _accountDeletionService = accountDeletionService;
    }

    /// <summary>
    /// Export all data belonging to the authenticated user as a versioned JSON package.
    /// The export includes: boards, notifications, capture items, proposals,
    /// chat sessions, audit trail, and preferences.
    /// </summary>
    [HttpGet("export")]
    [ResponseCache(NoStore = true)]
    public async Task<IActionResult> ExportUserData(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _dataExportService.ExportUserDataAsync(userId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Stream all data belonging to the authenticated user as a complete versioned JSON export.
    /// Unlike <c>GET /api/account/export</c>, this endpoint has no row cap and is suitable
    /// for users with more than 10,000 notifications, proposals, chat sessions, or audit entries.
    /// The JSON format is identical to the non-streaming endpoint.
    /// </summary>
    [HttpGet("export/stream")]
    [ResponseCache(NoStore = true)]
    public async Task<IActionResult> StreamUserData(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        // Validate the user exists before committing to streaming
        Response.ContentType = "application/json";
        Response.Headers.ContentDisposition = "attachment; filename=\"taskdeck-export.json\"";

        var result = await _dataExportService.StreamUserDataExportAsync(userId, Response.Body, cancellationToken);
        if (!result.IsSuccess)
        {
            // Guard: once any bytes have been flushed to the wire the status code (200) and
            // headers are committed — we cannot change them. Attempting to set a 4xx/5xx would
            // throw InvalidOperationException. In that case we fall through and return EmptyResult;
            // the client will receive a truncated/incomplete JSON body and must validate completeness.
            if (!Response.HasStarted)
                return result.ToErrorActionResult();
        }

        return new EmptyResult();
    }

    /// <summary>
    /// Request account deletion and anonymization. This is an irreversible operation.
    /// Requires re-authentication (current password) and an explicit confirmation phrase.
    /// </summary>
    [HttpPost("delete")]
    public async Task<IActionResult> DeleteAccount(
        [FromBody] AccountDeletionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _accountDeletionService.DeleteAccountAsync(userId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
