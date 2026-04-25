using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Connectors;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Connector provider discovery, health checks, and credential management.
/// All endpoints require authentication (GP-02).
/// </summary>
[ApiController]
[Authorize]
[Route("api/connectors")]
[Produces("application/json")]
public class ConnectorProvidersController : AuthenticatedControllerBase
{
    private readonly IConnectorProviderRegistry _providerRegistry;
    private readonly ConnectorExecutionService _executionService;
    private readonly IConnectorCredentialService _credentialService;

    public ConnectorProvidersController(
        IConnectorProviderRegistry providerRegistry,
        ConnectorExecutionService executionService,
        IConnectorCredentialService credentialService,
        IUserContext userContext)
        : base(userContext)
    {
        _providerRegistry = providerRegistry;
        _executionService = executionService;
        _credentialService = credentialService;
    }

    /// <summary>
    /// List all available connector providers.
    /// </summary>
    /// <response code="200">Providers listed.</response>
    /// <response code="401">Authentication required.</response>
    [HttpGet("providers")]
    [ProducesResponseType(typeof(IReadOnlyList<ConnectorProviderSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListProviders(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out _, out var errorResult))
            return errorResult!;

        var providers = _providerRegistry.GetAll();
        var summaries = new List<ConnectorProviderSummaryDto>();

        foreach (var provider in providers)
        {
            var capabilities = await provider.GetCapabilitiesAsync(cancellationToken);
            summaries.Add(new ConnectorProviderSummaryDto(
                provider.ProviderId,
                capabilities.DisplayName,
                provider.ConnectorType,
                provider.Direction,
                capabilities.Description));
        }

        return Ok(summaries);
    }

    /// <summary>
    /// Check the health of a specific connector provider.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Health check result.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Provider not found.</response>
    [HttpGet("providers/{providerId}/health")]
    [ProducesResponseType(typeof(ConnectorProviderHealthDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckProviderHealth(
        [StringLength(100, MinimumLength = 1)] string providerId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out _, out var errorResult))
            return errorResult!;

        if (string.IsNullOrWhiteSpace(providerId) || providerId.Length > 100)
        {
            return BadRequest(new ApiErrorResponse(
                "ValidationError",
                "Provider ID must be between 1 and 100 characters."));
        }

        var result = await _executionService.CheckProviderHealthAsync(providerId, cancellationToken);

        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        var health = result.Value;
        return Ok(new ConnectorProviderHealthDto(
            providerId,
            health.Status,
            health.Message,
            health.CheckedAt));
    }

    /// <summary>
    /// Store credentials for a connector instance.
    /// The plaintext value is encrypted before storage.
    /// </summary>
    /// <param name="connectorId">The connector instance ID.</param>
    /// <param name="dto">The credential to store.</param>
    /// <response code="201">Credential stored.</response>
    /// <response code="400">Invalid credential data.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Connector not found.</response>
    [HttpPost("{connectorId}/credentials")]
    [ProducesResponseType(typeof(ConnectorCredentialDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StoreCredential(
        Guid connectorId,
        [FromBody] StoreConnectorCredentialDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        if (string.IsNullOrWhiteSpace(dto.Label))
        {
            return BadRequest(new ApiErrorResponse("ValidationError", "Credential label must not be empty."));
        }

        if (dto.Label.Trim().Length > 100)
        {
            return BadRequest(new ApiErrorResponse(
                "ValidationError",
                "Credential label cannot exceed 100 characters."));
        }

        if (string.IsNullOrWhiteSpace(dto.Value))
        {
            return BadRequest(new ApiErrorResponse("ValidationError", "Credential value must not be empty."));
        }

        var result = await _credentialService.StoreCredentialAsync(
            connectorId,
            userId,
            dto.AuthMethod,
            dto.Label,
            dto.Value,
            dto.ExpiresAt);

        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>
    /// Remove credentials for a connector instance.
    /// </summary>
    /// <param name="connectorId">The connector instance ID.</param>
    /// <response code="204">Credential removed.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Credential not found.</response>
    [HttpDelete("{connectorId}/credentials")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCredential(Guid connectorId)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _credentialService.DeleteCredentialAsync(connectorId, userId);

        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        return NoContent();
    }
}
