using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Integrations registry endpoints: manage connector instances.
/// All inbound connectors route through the capture pipeline (GP-06).
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
[Produces("application/json")]
public class IntegrationsController : AuthenticatedControllerBase
{
    private readonly IIntegrationRegistryService _registryService;

    public IntegrationsController(
        IIntegrationRegistryService registryService,
        IUserContext userContext)
        : base(userContext)
    {
        _registryService = registryService;
    }

    /// <summary>
    /// List all connectors for the authenticated user.
    /// </summary>
    /// <returns>A list of connector summaries.</returns>
    /// <response code="200">Connectors listed successfully.</response>
    /// <response code="401">Authentication required.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<IntegrationConnectorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListConnectors()
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _registryService.ListConnectorsAsync(userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Get a connector's details including recent events.
    /// </summary>
    /// <param name="id">The connector ID.</param>
    /// <returns>Connector details with recent events.</returns>
    /// <response code="200">Connector details returned.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Connector not found.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(IntegrationConnectorDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConnector(Guid id)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _registryService.GetConnectorAsync(id, userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Register a new connector.
    /// </summary>
    /// <param name="dto">The connector configuration.</param>
    /// <returns>The newly created connector.</returns>
    /// <response code="201">Connector registered successfully.</response>
    /// <response code="400">Invalid connector configuration.</response>
    /// <response code="401">Authentication required.</response>
    [HttpPost]
    [ProducesResponseType(typeof(IntegrationConnectorDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RegisterConnector([FromBody] CreateIntegrationConnectorDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _registryService.RegisterConnectorAsync(userId, dto);
        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        return CreatedAtAction(
            nameof(GetConnector),
            new { id = result.Value.Id },
            result.Value);
    }

    /// <summary>
    /// Update a connector's name or configuration.
    /// </summary>
    /// <param name="id">The connector ID.</param>
    /// <param name="dto">The fields to update.</param>
    /// <returns>The updated connector.</returns>
    /// <response code="200">Connector updated successfully.</response>
    /// <response code="400">Invalid update data.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Connector not found.</response>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(IntegrationConnectorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateConnector(Guid id, [FromBody] UpdateIntegrationConnectorDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _registryService.UpdateConnectorAsync(id, userId, dto);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Remove a connector.
    /// </summary>
    /// <param name="id">The connector ID.</param>
    /// <returns>No content on success.</returns>
    /// <response code="204">Connector removed.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Connector not found.</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteConnector(Guid id)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _registryService.DeleteConnectorAsync(id, userId);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }

    /// <summary>
    /// Enable a connector.
    /// </summary>
    /// <param name="id">The connector ID.</param>
    /// <returns>The updated connector.</returns>
    /// <response code="200">Connector enabled.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Connector not found.</response>
    /// <response code="409">Connector is already active.</response>
    [HttpPost("{id}/enable")]
    [ProducesResponseType(typeof(IntegrationConnectorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EnableConnector(Guid id)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _registryService.EnableConnectorAsync(id, userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Disable a connector.
    /// </summary>
    /// <param name="id">The connector ID.</param>
    /// <returns>The updated connector.</returns>
    /// <response code="200">Connector disabled.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Connector not found.</response>
    /// <response code="409">Connector is already disabled.</response>
    [HttpPost("{id}/disable")]
    [ProducesResponseType(typeof(IntegrationConnectorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DisableConnector(Guid id)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _registryService.DisableConnectorAsync(id, userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
