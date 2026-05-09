using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Agents;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Service for hash-pinning MCP tool definitions.
/// Computes SHA-256 hashes of (name, description, inputSchema) and tracks
/// approval state. When a tool's definition changes, the hash changes and
/// user re-approval is required before the tool can be used.
/// </summary>
public sealed class McpToolDefinitionHashService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<McpToolDefinitionHashService>? _logger;

    public McpToolDefinitionHashService(
        IUnitOfWork unitOfWork,
        ILogger<McpToolDefinitionHashService>? logger = null)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger;
    }

    /// <summary>
    /// Computes a SHA-256 hash of the tool definition (name + description + schema).
    /// </summary>
    public static string ComputeDefinitionHash(string name, string description, string inputSchema)
    {
        var combined = $"{name}\n{description}\n{inputSchema}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Checks whether a tool definition is approved for use by the given user.
    /// Returns false if the tool has never been seen, if the definition has changed
    /// since approval, or if the user has not yet approved.
    /// </summary>
    public async Task<Result<bool>> IsToolApprovedAsync(
        Guid userId,
        string toolName,
        string currentHash,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<bool>(ErrorCodes.ValidationError, "UserId is required.");

        if (string.IsNullOrWhiteSpace(toolName))
            return Result.Failure<bool>(ErrorCodes.ValidationError, "ToolName is required.");

        if (string.IsNullOrWhiteSpace(currentHash))
            return Result.Failure<bool>(ErrorCodes.ValidationError, "CurrentHash is required.");

        var existing = await _unitOfWork.McpToolHashes.GetByUserAndToolAsync(userId, toolName, cancellationToken);
        if (existing is null)
        {
            return Result.Success(false);
        }

        return Result.Success(existing.IsDefinitionApproved(currentHash));
    }

    /// <summary>
    /// Records or updates a tool definition hash, and optionally approves it.
    /// If the definition has changed since last recorded, approval is revoked.
    /// </summary>
    public async Task<Result<McpToolHash>> RecordToolDefinitionAsync(
        Guid userId,
        string toolName,
        string definitionHash,
        bool approve = false,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<McpToolHash>(ErrorCodes.ValidationError, "UserId is required.");

        if (string.IsNullOrWhiteSpace(toolName))
            return Result.Failure<McpToolHash>(ErrorCodes.ValidationError, "ToolName is required.");

        if (string.IsNullOrWhiteSpace(definitionHash))
            return Result.Failure<McpToolHash>(ErrorCodes.ValidationError, "DefinitionHash is required.");

        var existing = await _unitOfWork.McpToolHashes.GetByUserAndToolAsync(userId, toolName, cancellationToken);

        if (existing is not null)
        {
            var previousHash = existing.DefinitionHash;
            existing.UpdateHash(definitionHash);

            if (previousHash != definitionHash)
            {
                _logger?.LogInformation(
                    "MCP tool '{ToolName}' definition changed for user '{UserId}'. Approval revoked, re-approval required.",
                    toolName, userId);
            }

            if (approve)
            {
                existing.Approve();
            }

            await _unitOfWork.McpToolHashes.UpdateAsync(existing, cancellationToken);
        }
        else
        {
            existing = new McpToolHash(userId, toolName, definitionHash);

            if (approve)
            {
                existing.Approve();
            }

            await _unitOfWork.McpToolHashes.AddAsync(existing, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(existing);
    }

    /// <summary>
    /// Approves a tool definition hash for a user. Returns failure if the tool
    /// has not been recorded yet.
    /// </summary>
    public async Task<Result> ApproveToolAsync(
        Guid userId,
        string toolName,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure(ErrorCodes.ValidationError, "UserId is required.");

        if (string.IsNullOrWhiteSpace(toolName))
            return Result.Failure(ErrorCodes.ValidationError, "ToolName is required.");

        var existing = await _unitOfWork.McpToolHashes.GetByUserAndToolAsync(userId, toolName, cancellationToken);
        if (existing is null)
            return Result.Failure(ErrorCodes.NotFound, $"Tool '{toolName}' has not been recorded yet.");

        existing.Approve();
        await _unitOfWork.McpToolHashes.UpdateAsync(existing, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "MCP tool '{ToolName}' approved for user '{UserId}'", toolName, userId);

        return Result.Success();
    }

    /// <summary>
    /// Returns all tool hashes for a user, for inspection.
    /// </summary>
    public async Task<Result<IReadOnlyList<McpToolHash>>> GetToolHashesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<IReadOnlyList<McpToolHash>>(ErrorCodes.ValidationError, "UserId is required.");

        var hashes = await _unitOfWork.McpToolHashes.GetByUserAsync(userId, cancellationToken);
        return Result.Success<IReadOnlyList<McpToolHash>>(hashes.ToList());
    }
}
