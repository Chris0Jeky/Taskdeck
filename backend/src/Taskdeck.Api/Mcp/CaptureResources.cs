using System.Text.Json;
using ModelContextProtocol.Server;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;

namespace Taskdeck.Api.Mcp;

/// <summary>
/// MCP resource provider for Taskdeck capture inbox.
/// Exposes pending capture items as read-only MCP resources.
/// </summary>
[McpServerResourceType]
public class CaptureResources
{
    private readonly ICaptureService _captureService;
    private readonly IUserContextProvider _userContext;

    public CaptureResources(
        ICaptureService captureService,
        IUserContextProvider userContext)
    {
        _captureService = captureService;
        _userContext = userContext;
    }

    /// <summary>
    /// Lists pending capture items for the current user.
    /// </summary>
    [McpServerResource(
        UriTemplate = "taskdeck://captures",
        Name = "captures",
        Title = "Capture Inbox",
        MimeType = "application/json")]
    public async Task<string> ListCaptures()
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        var result = await _captureService.ListAsync(userId, new CaptureListFilterDto());
        if (!result.IsSuccess)
            throw new InvalidOperationException($"MCP: failed to list captures: {PublicFailureMessage(result)}");

        var captures = result.Value.Select(c => new
        {
            id = c.Id,
            status = c.Status.ToString(),
            source = c.Source.ToString(),
            textExcerpt = c.TextExcerpt,
            boardId = c.BoardId,
            createdAt = c.CreatedAt,
            processedAt = c.ProcessedAt
        });

        return JsonSerializer.Serialize(new
        {
            captures,
            totalCount = result.Value.Count
        }, BoardResources.SerializerOptions);
    }

    /// <summary>
    /// Returns detail for a single capture item.
    /// </summary>
    [McpServerResource(
        UriTemplate = "taskdeck://captures/{captureId}",
        Name = "capture_detail",
        Title = "Capture Item Detail",
        MimeType = "application/json")]
    public async Task<string> GetCaptureDetail(string captureId)
    {
        var userId = await _userContext.GetCurrentUserIdAsync();

        if (!Guid.TryParse(captureId, out var captureGuid))
            throw new ArgumentException($"MCP: invalid capture ID '{captureId}'");

        var result = await _captureService.GetByIdAsync(userId, captureGuid);
        if (!result.IsSuccess)
            throw new InvalidOperationException($"MCP: failed to get capture: {PublicFailureMessage(result)}");

        var c = result.Value;

        return JsonSerializer.Serialize(new
        {
            id = c.Id,
            userId = c.UserId,
            boardId = c.BoardId,
            status = c.Status.ToString(),
            source = c.Source.ToString(),
            rawText = c.RawText,
            textExcerpt = c.TextExcerpt,
            createdAt = c.CreatedAt,
            processedAt = c.ProcessedAt,
            retryCount = c.RetryCount,
            errorMessage = c.ErrorMessage
        }, BoardResources.SerializerOptions);
    }

    private static string PublicFailureMessage(Result result) =>
        SensitiveDataRedactor.SanitizeLlmFailureMessage(result.ErrorCode, result.ErrorMessage);
}
