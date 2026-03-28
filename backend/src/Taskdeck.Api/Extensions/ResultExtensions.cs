using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Extensions;

public static class ResultExtensions
{
    public static int ToHttpStatusCode(this Result result)
    {
        return result.ErrorCode switch
        {
            ErrorCodes.NotFound => StatusCodes.Status404NotFound,
            ErrorCodes.ValidationError => StatusCodes.Status400BadRequest,
            ErrorCodes.WipLimitExceeded => StatusCodes.Status400BadRequest,
            ErrorCodes.AuthenticationFailed => StatusCodes.Status401Unauthorized,
            ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
            ErrorCodes.TooManyRequests => StatusCodes.Status429TooManyRequests,
            ErrorCodes.LlmQuotaExceeded => StatusCodes.Status429TooManyRequests,
            ErrorCodes.LlmKillSwitchActive => StatusCodes.Status503ServiceUnavailable,
            ErrorCodes.Conflict => StatusCodes.Status409Conflict,
            ErrorCodes.InvalidOperation => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
    }

    /// <summary>
    /// Maps a failed Result to the appropriate IActionResult based on the error code.
    /// Should only be called when result.IsSuccess is false.
    /// </summary>
    public static IActionResult ToErrorActionResult(this Result result)
    {
        var statusCode = result.ToHttpStatusCode();
        var body = ApiErrorResponse.FromResult(result);

        return statusCode switch
        {
            StatusCodes.Status404NotFound => new NotFoundObjectResult(body),
            StatusCodes.Status400BadRequest => new BadRequestObjectResult(body),
            StatusCodes.Status401Unauthorized => new UnauthorizedObjectResult(body),
            StatusCodes.Status403Forbidden => new ObjectResult(body) { StatusCode = StatusCodes.Status403Forbidden },
            StatusCodes.Status429TooManyRequests => new ObjectResult(body) { StatusCode = StatusCodes.Status429TooManyRequests },
            StatusCodes.Status503ServiceUnavailable => new ObjectResult(body) { StatusCode = StatusCodes.Status503ServiceUnavailable },
            StatusCodes.Status409Conflict => new ConflictObjectResult(body),
            _ => new ObjectResult(new ApiErrorResponse(
                ErrorCodes.UnexpectedError,
                "An unexpected error occurred."))
            {
                StatusCode = StatusCodes.Status500InternalServerError
            }
        };
    }
}
