using Microsoft.AspNetCore.Mvc;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Extensions;

public static class ResultExtensions
{
    /// <summary>
    /// Maps a failed Result to the appropriate IActionResult based on the error code.
    /// Should only be called when result.IsSuccess is false.
    /// </summary>
    public static IActionResult ToErrorActionResult(this Result result)
    {
        var body = new { errorCode = result.ErrorCode, message = result.ErrorMessage };

        return result.ErrorCode switch
        {
            ErrorCodes.NotFound => new NotFoundObjectResult(body),
            ErrorCodes.ValidationError => new BadRequestObjectResult(body),
            ErrorCodes.WipLimitExceeded => new BadRequestObjectResult(body),
            ErrorCodes.AuthenticationFailed => new UnauthorizedObjectResult(body),
            ErrorCodes.Unauthorized => new UnauthorizedObjectResult(body),
            ErrorCodes.Forbidden => new ObjectResult(body) { StatusCode = 403 },
            ErrorCodes.Conflict => new ConflictObjectResult(body),
            ErrorCodes.InvalidOperation => new ConflictObjectResult(body),
            _ => new ObjectResult(new ProblemDetails
            {
                Detail = result.ErrorMessage,
                Status = 500
            }) { StatusCode = 500 }
        };
    }
}
