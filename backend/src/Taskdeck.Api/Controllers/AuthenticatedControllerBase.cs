using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Base controller for endpoints that require an authenticated user context.
/// Provides a shared helper to extract and validate the current user's ID from JWT claims.
/// </summary>
public abstract class AuthenticatedControllerBase : ControllerBase
{
    protected readonly IUserContext UserContext;

    protected AuthenticatedControllerBase(IUserContext userContext)
    {
        UserContext = userContext;
    }

    protected bool TryGetCurrentUserId(out Guid userId, out IActionResult? errorResult)
    {
        userId = Guid.Empty;
        errorResult = null;

        if (!UserContext.IsAuthenticated || string.IsNullOrWhiteSpace(UserContext.UserId))
        {
            errorResult = Unauthorized(new ApiErrorResponse(
                ErrorCodes.AuthenticationFailed,
                "Authenticated user context is required"));
            return false;
        }

        if (!Guid.TryParse(UserContext.UserId, out userId))
        {
            errorResult = Unauthorized(new ApiErrorResponse(
                ErrorCodes.AuthenticationFailed,
                "Authenticated user id claim is invalid"));
            return false;
        }

        return true;
    }

    protected async Task<IActionResult?> EnsureBoardPermissionAsync(
        IAuthorizationService authorizationService,
        Guid userId,
        Guid boardId,
        Func<IAuthorizationService, Guid, Guid, Task<Result<bool>>> permissionCheck,
        string forbiddenMessage)
    {
        var permission = await permissionCheck(authorizationService, userId, boardId);
        if (!permission.IsSuccess)
            return permission.ToErrorActionResult();

        if (permission.Value)
            return null;

        var forbidden = Result.Failure(ErrorCodes.Forbidden, forbiddenMessage);
        return forbidden.ToErrorActionResult();
    }
}
