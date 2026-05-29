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
/// <remarks>
/// Authorization conventions (enforced by Taskdeck.Architecture.Tests.ApiControllerBoundaryTests):
/// <list type="bullet">
/// <item>Every user-facing controller derives from this base and declares a class-level
/// <c>[Authorize]</c>; identity is resolved only from JWT claims via <see cref="TryGetCurrentUserId"/>,
/// never from request input.</item>
/// <item>Controllers without a class-level <c>[Authorize]</c> (only AuthController and HealthController)
/// must mark every action explicitly with <c>[Authorize]</c> or <c>[AllowAnonymous]</c>.</item>
/// <item>Board-scoped authorization is performed in the controller via
/// <see cref="EnsureBoardPermissionAsync"/> as the preferred convention; services that re-check a
/// board/user grant internally do so as defense in depth, not as a substitute.</item>
/// </list>
/// </remarks>
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
