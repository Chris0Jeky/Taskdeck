using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Taskdeck.Api.Mcp;
using Taskdeck.Api.Middleware;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Mcp;

namespace Taskdeck.Api.Extensions;

/// <summary>
/// Registers the shared set of MCP resources and tools used by all three
/// hosting modes (co-hosted web, standalone HTTP, and stdio).
/// </summary>
public static class McpResourcesAndToolsRegistration
{
    private const string AccessDeniedMessage = "Access denied for this MCP operation.";

    private static readonly IReadOnlyDictionary<string, ApiKeyScope> ToolScopes =
        new Dictionary<string, ApiKeyScope>(StringComparer.Ordinal)
        {
            ["search_cards"] = ApiKeyScope.Read,
            ["get_board_summary"] = ApiKeyScope.Read,
            ["get_proposal_status"] = ApiKeyScope.Read,
            ["list_proposals"] = ApiKeyScope.Read,
            ["create_card"] = ApiKeyScope.Propose,
            ["move_card"] = ApiKeyScope.Propose,
            ["update_card"] = ApiKeyScope.Propose,
            ["archive_card"] = ApiKeyScope.Propose,
            ["create_column"] = ApiKeyScope.Propose,
            ["dismiss_proposal"] = ApiKeyScope.Manage,
            ["create_capture"] = ApiKeyScope.Manage
        };

    /// <summary>
    /// Add MCP resources (Board, Capture, Proposal) and tools (Read, Write, Proposal)
    /// to the given MCP server builder.
    /// </summary>
    public static IMcpServerBuilder AddMcpResourcesAndTools(this IMcpServerBuilder builder)
    {
        return builder
            .WithMessageFilters(filters =>
            {
                filters.AddIncomingFilter(next => async (context, cancellationToken) =>
                {
                    if (context.JsonRpcMessage is JsonRpcRequest request
                        && request.Method == RequestMethods.ToolsCall
                        && !GetRequiredToolScope(request).HasValue)
                    {
                        // Unknown, missing, and unclassified tool names all fail before the SDK's
                        // primitive matcher. Full is a mask of the three known capability bits,
                        // not permission to invoke future tools that have no explicit mapping.
                        // Capability checks for known tools run in AddCallToolFilter below, where
                        // context.Services is the SDK-created request scope rather than the server root.
                        await SendToolAccessDeniedAsync(context, request, cancellationToken);
                        return;
                    }

                    await next(context, cancellationToken);
                });
            })
            .WithRequestFilters(filters =>
            {
                filters.AddCallToolFilter(next => async (context, cancellationToken) =>
                {
                    var required = GetRequiredToolScope(context.Params?.Name);
                    if (!required.HasValue)
                        return CreateToolAccessDeniedResult();

                    ApiKeyScope granted;
                    try
                    {
                        granted = await GetValidatedScopesAsync(
                            context.Services,
                            context.User,
                            cancellationToken,
                            preserveStdioIdentityFailure: true);
                    }
                    catch (StdioIdentityResolutionException exception)
                    {
                        return CreateToolErrorResult(exception.Message);
                    }

                    if (!ApiKeyScopeRules.Includes(granted, required.Value))
                        return CreateToolAccessDeniedResult();

                    return await next(context, cancellationToken);
                });

                filters.AddReadResourceFilter(next => async (context, cancellationToken) =>
                {
                    var granted = await GetValidatedScopesAsync(
                        context.Services,
                        context.User,
                        cancellationToken);
                    if (!ApiKeyScopeRules.Includes(granted, ApiKeyScope.Read))
                        throw new McpException(AccessDeniedMessage);

                    return await next(context, cancellationToken);
                });

                filters.AddListToolsFilter(next => async (context, cancellationToken) =>
                {
                    var granted = await GetValidatedScopesAsync(
                        context.Services,
                        context.User,
                        cancellationToken);
                    var result = await next(context, cancellationToken);
                    result.Tools = result.Tools
                        .Where(tool =>
                            ToolScopes.TryGetValue(tool.Name, out var required)
                            && ApiKeyScopeRules.Includes(granted, required))
                        .ToList();
                    return result;
                });

                filters.AddListResourcesFilter(next => async (context, cancellationToken) =>
                {
                    var granted = await GetValidatedScopesAsync(
                        context.Services,
                        context.User,
                        cancellationToken);
                    if (!ApiKeyScopeRules.Includes(granted, ApiKeyScope.Read))
                        return new ListResourcesResult { Resources = [] };

                    return await next(context, cancellationToken);
                });

                filters.AddListResourceTemplatesFilter(next => async (context, cancellationToken) =>
                {
                    var granted = await GetValidatedScopesAsync(
                        context.Services,
                        context.User,
                        cancellationToken);
                    if (!ApiKeyScopeRules.Includes(granted, ApiKeyScope.Read))
                        return new ListResourceTemplatesResult { ResourceTemplates = [] };

                    return await next(context, cancellationToken);
                });
            })
            .WithResources<BoardResources>()
            .WithResources<CaptureResources>()
            .WithResources<ProposalResources>()
            .WithTools<ReadTools>()
            .WithTools<WriteTools>()
            .WithTools<ProposalTools>();
    }

    private static ApiKeyScope? GetRequiredToolScope(JsonRpcRequest request)
    {
        var name = GetRequestedToolName(request);
        return name is not null && ToolScopes.TryGetValue(name, out var required)
            ? required
            : null;
    }

    private static string? GetRequestedToolName(JsonRpcRequest request)
    {
        return request.Params is JsonObject parameters
            && parameters["name"] is JsonValue nameNode
            && nameNode.TryGetValue<string>(out var name)
                ? name
                : null;
    }

    private static ApiKeyScope? GetRequiredToolScope(string? name)
    {
        return name is not null && ToolScopes.TryGetValue(name, out var required)
            ? required
            : null;
    }

    private static Task SendToolAccessDeniedAsync(
        MessageContext context,
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        return context.Server.SendMessageAsync(
            new JsonRpcResponse
            {
                Id = request.Id,
                Result = new JsonObject
                {
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = AccessDeniedMessage
                        }
                    },
                    ["isError"] = true
                }
            },
            cancellationToken);
    }

    private static CallToolResult CreateToolAccessDeniedResult()
    {
        return CreateToolErrorResult(AccessDeniedMessage);
    }

    private static CallToolResult CreateToolErrorResult(string message)
    {
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],
            IsError = true
        };
    }

    private static async Task<ApiKeyScope> GetValidatedScopesAsync(
        IServiceProvider? services,
        ClaimsPrincipal? principal,
        CancellationToken cancellationToken,
        bool preserveStdioIdentityFailure = false)
    {
        var scopeClaim = principal?.FindFirst(ApiKeyMiddleware.ScopesClaimType)?.Value;
        if (scopeClaim is not null)
        {
            if (int.TryParse(
                    scopeClaim,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var rawScopes))
            {
                var claimedScopes = (ApiKeyScope)rawScopes;
                if (ApiKeyScopeRules.IsValid(claimedScopes))
                    return claimedScopes;
            }

            throw new McpException(AccessDeniedMessage);
        }

        var provider = services?.GetService<IUserContextProvider>();
        if (provider is null)
            throw new McpException(AccessDeniedMessage);

        McpUserContext current;
        try
        {
            current = await provider.GetCurrentContextAsync(cancellationToken);
        }
        catch (StdioIdentityResolutionException) when (preserveStdioIdentityFailure)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw new McpException(AccessDeniedMessage);
        }

        if (current.UserId == Guid.Empty || !ApiKeyScopeRules.IsValid(current.Scopes))
            throw new McpException(AccessDeniedMessage);

        return current.Scopes;
    }
}
