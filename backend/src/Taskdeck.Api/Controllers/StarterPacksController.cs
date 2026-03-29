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
[Route("api/boards/{boardId}/starter-packs")]
public class StarterPacksController : AuthenticatedControllerBase
{
    private readonly IStarterPackApplyService _starterPackApplyService;
    private readonly IStarterPackCatalogService _starterPackCatalogService;
    private readonly IStarterPackManifestValidator _starterPackManifestValidator;
    private readonly BoardAuthorizationService _authorizationService;

    public StarterPacksController(
        IStarterPackApplyService starterPackApplyService,
        IStarterPackCatalogService starterPackCatalogService,
        IStarterPackManifestValidator starterPackManifestValidator,
        BoardAuthorizationService authorizationService,
        IUserContext userContext)
        : base(userContext)
    {
        _starterPackApplyService = starterPackApplyService;
        _starterPackCatalogService = starterPackCatalogService;
        _starterPackManifestValidator = starterPackManifestValidator;
        _authorizationService = authorizationService;
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog(Guid boardId)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
        {
            return errorResult!;
        }

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanReadBoardAsync(actorId, targetBoardId),
            "You do not have permission to view this board");

        if (permissionError is not null)
        {
            return permissionError;
        }

        return Ok(_starterPackCatalogService.GetCatalog());
    }

    [HttpPost("validate-manifest")]
    public async Task<IActionResult> ValidateManifest(
        Guid boardId,
        [FromBody] ValidateManifestJsonDto? dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
        {
            return errorResult!;
        }

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanReadBoardAsync(actorId, targetBoardId),
            "You do not have permission to view this board");

        if (permissionError is not null)
        {
            return permissionError;
        }

        if (dto == null || string.IsNullOrWhiteSpace(dto.ManifestJson))
        {
            return Result.Failure(ErrorCodes.ValidationError, "Manifest JSON is required.").ToErrorActionResult();
        }

        var result = _starterPackManifestValidator.ValidateJson(dto.ManifestJson);
        return Ok(new ValidateManifestResultDto(
            result.IsValid,
            result.Manifest,
            result.Errors.Select(e => new ManifestValidationErrorDto(e.Path, e.Message)).ToList()));
    }

    [HttpPost("apply")]
    public async Task<IActionResult> ApplyStarterPack(
        Guid boardId,
        [FromBody] ApplyStarterPackDto? dto,
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
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanWriteBoardAsync(actorId, targetBoardId),
            "You do not have permission to modify this board");

        if (permissionError is not null)
        {
            return permissionError;
        }

        if (dto == null)
        {
            return Result.Failure(ErrorCodes.ValidationError, "Request body is required.").ToErrorActionResult();
        }

        var result = await _starterPackApplyService.ApplyToBoardAsync(boardId, dto, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ToErrorActionResult();
        }

        if (!dto.DryRun && result.Value.HasBlockingConflicts)
        {
            return Conflict(result.Value);
        }

        return Ok(result.Value);
    }
}
