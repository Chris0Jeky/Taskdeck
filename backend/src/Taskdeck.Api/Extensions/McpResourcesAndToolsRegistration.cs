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
                    if (context.JsonRpcMessage is JsonRpcRequest request)
                    {
                        ApiKeyScope? required = request.Method switch
                        {
                            RequestMethods.ToolsCall => GetRequiredToolScope(request),
                            RequestMethods.ResourcesRead => ApiKeyScope.Read,
                            _ => null
                        };

                        if (required.HasValue)
                        {
                            var granted = await GetValidatedScopesAsync(
                                context.Services,
                                context.User,
                                cancellationToken);
                            if (!ApiKeyScopeRules.Includes(granted, required.Value))
                            {
                                await SendAccessDeniedAsync(context, request, cancellationToken);
                                return;
                            }
                        }
                        else if (request.Method == RequestMethods.ToolsCall)
                        {
                            // Unknown, missing, and unclassified tool names all fail before the SDK's
                            // primitive matcher. Full is a mask of the three known capability bits,
                            // not permission to invoke future tools that have no explicit mapping.
                            await SendAccessDeniedAsync(context, request, cancellationToken);
                            return;
                        }
                    }

                    await next(context, cancellationToken);
                });
            })
            .WithRequestFilters(filters =>
            {
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
        if (request.Params is not JsonObject parameters
            || parameters["name"] is not JsonValue nameNode
            || !nameNode.TryGetValue<string>(out var name))
        {
            return null;
        }

        return ToolScopes.TryGetValue(name, out var required) ? required : null;
    }

    private static Task SendAccessDeniedAsync(
        MessageContext context,
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        return context.Server.SendMessageAsync(
            new JsonRpcError
            {
                Id = request.Id,
                Error = new JsonRpcErrorDetail
                {
                    Code = (int)McpErrorCode.InvalidRequest,
                    Message = AccessDeniedMessage
                }
            },
            cancellationToken);
    }

    private static async Task<ApiKeyScope> GetValidatedScopesAsync(
        IServiceProvider? services,
        ClaimsPrincipal? principal,
        CancellationToken cancellationToken)
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
        catch (InvalidOperationException)
        {
            throw new McpException(AccessDeniedMessage);
        }

        if (current.UserId == Guid.Empty || !ApiKeyScopeRules.IsValid(current.Scopes))
            throw new McpException(AccessDeniedMessage);

        return current.Scopes;
    }
}
