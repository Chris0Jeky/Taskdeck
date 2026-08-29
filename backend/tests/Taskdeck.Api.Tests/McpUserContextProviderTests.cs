using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Mcp;
using Xunit;

namespace Taskdeck.Api.Tests;

public class McpUserContextProviderTests
{
    [Fact]
    public async Task HttpProvider_ReturnsPersistedValidatedIdentityAndScopes()
    {
        var userId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        httpContext.Items[HttpUserContextProvider.UserIdItemKey] = userId;
        httpContext.Items[HttpUserContextProvider.ScopesItemKey] =
            ApiKeyScope.Read | ApiKeyScope.Manage;
        var provider = CreateProvider(httpContext);

        var current = await provider.GetCurrentContextAsync();

        current.Should().Be(new McpUserContext(
            userId,
            ApiKeyScope.Read | ApiKeyScope.Manage));
        (await provider.GetCurrentUserIdAsync()).Should().Be(userId);
        (await provider.GetUserIdAsync()).Should().Be(userId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(8)]
    public async Task HttpProvider_FailsClosed_WhenScopeContextIsAbsentOrInvalid(int? rawMask)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[HttpUserContextProvider.UserIdItemKey] = Guid.NewGuid();
        if (rawMask.HasValue)
        {
            httpContext.Items[HttpUserContextProvider.ScopesItemKey] =
                (ApiKeyScope)rawMask.Value;
        }

        var provider = CreateProvider(httpContext);

        var act = () => provider.GetCurrentContextAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no validated API key context*");
        (await provider.GetUserIdAsync()).Should().BeNull();
    }

    private static HttpUserContextProvider CreateProvider(HttpContext httpContext)
    {
        return new HttpUserContextProvider(
            new HttpContextAccessor { HttpContext = httpContext },
            NullLogger<HttpUserContextProvider>.Instance);
    }
}
