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
[Route("api/boards/{boardId}/imports/external")]
public class ExternalImportsController : AuthenticatedControllerBase
{
    private readonly IExternalImportService _externalImportService;
    private readonly BoardAuthorizationService _authorizationService;

    public ExternalImportsController(
        IExternalImportService externalImportService,
        BoardAuthorizationService authorizationService,
        IUserContext userContext)
        : base(userContext)
    {
        _externalImportService = externalImportService;
        _authorizationService = authorizationService;
    }

    [HttpPost]
    public async Task<IActionResult> ImportToBoard(
        Guid boardId,
        [FromBody] ExternalImportRequestDto? dto,
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
            CanWriteBoardWithoutExistenceLeakAsync,
            "You do not have permission to modify this board");

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

        var result = await _externalImportService.ImportToBoardAsync(boardId, dto, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ToErrorActionResult();
        }

        if (!dto.DryRun && result.Value.HasConflicts)
        {
            return Conflict(result.Value);
        }

        return Ok(result.Value);
    }

    private static async Task<Result<bool>> CanWriteBoardWithoutExistenceLeakAsync(
        BoardAuthorizationService authorizationService,
        Guid actorId,
        Guid targetBoardId)
    {
        var permission = await authorizationService.CanWriteBoardAsync(actorId, targetBoardId);
        if (!permission.IsSuccess && permission.ErrorCode == ErrorCodes.NotFound)
        {
            return Result.Success<bool>(false);
        }

        return permission;
    }
}
