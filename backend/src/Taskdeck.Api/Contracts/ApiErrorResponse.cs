using Taskdeck.Domain.Common;

namespace Taskdeck.Api.Contracts;

public sealed record ApiErrorResponse(string ErrorCode, string Message)
{
    public static ApiErrorResponse FromResult(Result result)
    {
        return new ApiErrorResponse(result.ErrorCode, result.ErrorMessage);
    }
}
