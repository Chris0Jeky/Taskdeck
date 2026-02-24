using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;
using BoardAuthorizationService = Taskdeck.Application.Services.IAuthorizationService;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/boards/{boardId}/webhooks")]
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

    [HttpGet]
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

    [HttpPost]
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

    [HttpPost("{subscriptionId:guid}/rotate-secret")]
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

    [HttpDelete("{subscriptionId:guid}")]
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
