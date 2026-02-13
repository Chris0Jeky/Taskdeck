using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ResultExtensionsTests
{
    [Theory]
    [InlineData(ErrorCodes.NotFound, 404)]
    [InlineData(ErrorCodes.ValidationError, 400)]
    [InlineData(ErrorCodes.WipLimitExceeded, 400)]
    [InlineData(ErrorCodes.AuthenticationFailed, 401)]
    [InlineData(ErrorCodes.Unauthorized, 401)]
    [InlineData(ErrorCodes.Forbidden, 403)]
    [InlineData(ErrorCodes.Conflict, 409)]
    [InlineData(ErrorCodes.InvalidOperation, 409)]
    [InlineData("SomethingUnexpected", 500)]
    public void ToHttpStatusCode_ShouldMapErrorCodes(string errorCode, int expectedStatusCode)
    {
        var result = Result.Failure(errorCode, "test message");

        var statusCode = result.ToHttpStatusCode();

        statusCode.Should().Be(expectedStatusCode);
    }

    [Theory]
    [InlineData(ErrorCodes.NotFound, 404)]
    [InlineData(ErrorCodes.ValidationError, 400)]
    [InlineData(ErrorCodes.WipLimitExceeded, 400)]
    [InlineData(ErrorCodes.AuthenticationFailed, 401)]
    [InlineData(ErrorCodes.Unauthorized, 401)]
    [InlineData(ErrorCodes.Forbidden, 403)]
    [InlineData(ErrorCodes.Conflict, 409)]
    [InlineData(ErrorCodes.InvalidOperation, 409)]
    public void ToErrorActionResult_ShouldMapKnownErrorCodes(string errorCode, int expectedStatusCode)
    {
        var result = Result.Failure(errorCode, "test message");

        var actionResult = result.ToErrorActionResult();

        var objectResult = actionResult as ObjectResult;
        objectResult.Should().NotBeNull();
        objectResult!.StatusCode.Should().Be(expectedStatusCode);
    }

    [Fact]
    public void ToErrorActionResult_ShouldReturn500_ForUnknownErrorCode()
    {
        var result = Result.Failure("SomethingUnexpected", "unknown error");

        var actionResult = result.ToErrorActionResult();

        var objectResult = actionResult as ObjectResult;
        objectResult.Should().NotBeNull();
        objectResult!.StatusCode.Should().Be(500);
    }

    [Fact]
    public void ToErrorActionResult_ShouldIncludeErrorCodeAndMessage_ForNotFound()
    {
        var result = Result.Failure(ErrorCodes.NotFound, "Board not found");

        var actionResult = result.ToErrorActionResult();

        var notFoundResult = actionResult as NotFoundObjectResult;
        notFoundResult.Should().NotBeNull();

        notFoundResult!.Value.Should().BeOfType<ApiErrorResponse>();
        var body = (ApiErrorResponse)notFoundResult.Value!;
        body.ErrorCode.Should().Be(ErrorCodes.NotFound);
        body.Message.Should().Be("Board not found");
    }

    [Fact]
    public void ToErrorActionResult_GenericResult_ShouldMapErrorCodes()
    {
        var result = Result.Failure<string>(ErrorCodes.ValidationError, "Invalid input");

        var actionResult = result.ToErrorActionResult();

        var badRequestResult = actionResult as BadRequestObjectResult;
        badRequestResult.Should().NotBeNull();
        badRequestResult!.StatusCode.Should().Be(400);
    }
}
