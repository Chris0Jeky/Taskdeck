using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class UsersController : AuthenticatedControllerBase
{
    private readonly UserService _userService;

    public UsersController(UserService userService, IUserContext userContext)
        : base(userContext)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var currentUserResult = await _userService.GetUserByIdAsync(userId);
        if (!currentUserResult.IsSuccess)
            return currentUserResult.ToErrorActionResult();

        return Ok(new[] { currentUserResult.Value });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(Guid id)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        if (id != userId)
            return ForbiddenSelfScope();

        var result = await _userService.GetUserByIdAsync(id);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("by-username/{username}")]
    public async Task<IActionResult> GetUserByUsername(string username)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var currentUserResult = await _userService.GetUserByIdAsync(userId);
        if (!currentUserResult.IsSuccess)
            return currentUserResult.ToErrorActionResult();

        if (!string.Equals(currentUserResult.Value.Username, username, StringComparison.OrdinalIgnoreCase))
            return ForbiddenSelfScope();

        return Ok(currentUserResult.Value);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
    {
        var result = await _userService.CreateUserAsync(dto);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetUser), new { id = result.Value.Id }, result.Value)
            : result.ToErrorActionResult();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        if (id != userId)
            return ForbiddenSelfScope();

        var result = await _userService.UpdateUserAsync(id, dto);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost("{id}/deactivate")]
    public async Task<IActionResult> DeactivateUser(Guid id)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        if (id != userId)
            return ForbiddenSelfScope();

        var result = await _userService.DeactivateUserAsync(id);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }

    [HttpPost("{id}/activate")]
    public async Task<IActionResult> ActivateUser(Guid id)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        if (id != userId)
            return ForbiddenSelfScope();

        var result = await _userService.ActivateUserAsync(id);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }

    private static IActionResult ForbiddenSelfScope()
    {
        var forbidden = Result.Failure(ErrorCodes.Forbidden, "You can only access your own user profile");
        return forbidden.ToErrorActionResult();
    }
}
