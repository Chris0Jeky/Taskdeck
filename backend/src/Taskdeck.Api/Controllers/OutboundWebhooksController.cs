using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;
using BoardAuthorizationService = Taskdeck.Application.Services.IAuthorizationService;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Board-scoped outbound webhook subscriptions. Webhooks deliver signed event
/// payloads to external endpoints when board mutations occur. Consumers verify
/// delivery authenticity using HMAC-SHA256 signatures.
/// </summary>
[ApiController]
[Authorize]
[Route("api/boards/{boardId}/webhooks")]
[Produces("application/json")]
public class OutboundWebhooksController : AuthenticatedControllerBase
{
    private readonly IOutboundWebhookService _outboundWebhookService;
    private readonly BoardAuthorizationService _authorizationService;

    public OutboundWebhooksController(
        IOutboundWebhookService outboundWebhookService,
        BoardAuthorizationService authorizationService,
        IUserContext userContext)
        : base(userContext)
    {
        _outboundWebhookService = outboundWebhookService;
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// List all webhook subscriptions for a board.
    /// </summary>
    /// <param name="boardId">The board identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of webhook subscriptions.</returns>
    /// <response code="200">Returns the webhook subscriptions.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">User does not have permission to manage webhooks on this board.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<OutboundWebhookSubscriptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListSubscriptions(
        Guid boardId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
        {
            return errorResult!;
        }

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) =>
                authorizationService.CanManageBoardAccessAsync(actorId, targetBoardId),
            "You do not have permission to manage outbound webhooks for this board");
        if (permissionError is not null)
        {
            return permissionError;
        }

        var result = await _outboundWebhookService.ListSubscriptionsAsync(boardId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Create a new webhook subscription for a board. Returns the subscription
    /// along with its signing secret (shown only once).
    /// </summary>
    /// <param name="boardId">The board identifier.</param>
    /// <param name="dto">Subscription parameters: endpoint URL and optional event type filters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created subscription with its signing secret.</returns>
    /// <response code="201">Subscription created. The signing secret is included in the response and will not be shown again.</response>
    /// <response code="400">Validation error (e.g., invalid endpoint URL).</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">User does not have permission to manage webhooks on this board.</response>
    [HttpPost]
    [ProducesResponseType(typeof(OutboundWebhookSubscriptionSecretDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateSubscription(
        Guid boardId,
        [FromBody] CreateOutboundWebhookSubscriptionDto? dto,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
        {
            return errorResult!;
        }

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) =>
                authorizationService.CanManageBoardAccessAsync(actorId, targetBoardId),
            "You do not have permission to manage outbound webhooks for this board");
        if (permissionError is not null)
        {
            return permissionError;
        }

        if (dto == null)
        {
            return Result
                .Failure(ErrorCodes.ValidationError, "Request body is required.")
                .ToErrorActionResult();
        }

        var result = await _outboundWebhookService.CreateSubscriptionAsync(boardId, userId, dto, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(ListSubscriptions), new { boardId }, result.Value)
            : result.ToErrorActionResult();
    }

    /// <summary>
    /// Rotate the signing secret for a webhook subscription. The new secret is
    /// returned in the response and will not be shown again.
    /// </summary>
    /// <param name="boardId">The board identifier.</param>
    /// <param name="subscriptionId">The subscription identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The subscription with the new signing secret.</returns>
    /// <response code="200">Secret rotated successfully.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">User does not have permission to manage webhooks on this board.</response>
    /// <response code="404">Subscription not found.</response>
    [HttpPost("{subscriptionId:guid}/rotate-secret")]
    [ProducesResponseType(typeof(OutboundWebhookSubscriptionSecretDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RotateSecret(
        Guid boardId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
        {
            return errorResult!;
        }

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) =>
                authorizationService.CanManageBoardAccessAsync(actorId, targetBoardId),
            "You do not have permission to manage outbound webhooks for this board");
        if (permissionError is not null)
        {
            return permissionError;
        }

        var result = await _outboundWebhookService.RotateSecretAsync(
            boardId,
            subscriptionId,
            userId,
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Revoke (deactivate) a webhook subscription. Pending deliveries will be cancelled.
    /// </summary>
    /// <param name="boardId">The board identifier.</param>
    /// <param name="subscriptionId">The subscription identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">Subscription revoked successfully.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">User does not have permission to manage webhooks on this board.</response>
    /// <response code="404">Subscription not found.</response>
    [HttpDelete("{subscriptionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeSubscription(
        Guid boardId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
        {
            return errorResult!;
        }

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) =>
                authorizationService.CanManageBoardAccessAsync(actorId, targetBoardId),
            "You do not have permission to manage outbound webhooks for this board");
        if (permissionError is not null)
        {
            return permissionError;
        }

        var result = await _outboundWebhookService.RevokeSubscriptionAsync(
            boardId,
            subscriptionId,
            userId,
            cancellationToken);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }
}
