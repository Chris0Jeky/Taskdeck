using System.Security.Cryptography;
using System.Text;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Agents;

namespace Taskdeck.Application.Services;

/// <summary>
/// Service for MCP tool definition hash-pinning. Computes SHA-256 hashes
/// of tool definitions (name + description + inputSchema) and manages
/// user approval state. When a tool definition changes, the user must
/// re-approve before the tool can be used (GP-10: MCP integrity).
/// </summary>
public sealed class McpToolDefinitionHashService
{
    private readonly IUnitOfWork _unitOfWork;

    public McpToolDefinitionHashService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <summary>
    /// Computes a SHA-256 hash of the tool definition (name + description + inputSchema).
    /// Deterministic for the same inputs. Returns lowercase hex string (64 chars).
    /// </summary>
    public static string ComputeDefinitionHash(string toolName, string description, string inputSchema)
    {
        var input = $"{toolName}\n{description}\n{inputSchema}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Checks whether the given tool definition is approved for the user.
    /// Returns true only if the hash matches the stored+approved hash.
    /// </summary>
    public async Task<bool> IsToolApprovedAsync(
        Guid userId, string toolName, string currentHash,
        CancellationToken cancellationToken = default)
    {
        var existing = await _unitOfWork.McpToolHashes
            .GetByUserAndToolAsync(userId, toolName, cancellationToken);

        return existing is not null && existing.IsDefinitionApproved(currentHash);
    }

    /// <summary>
    /// Records a tool definition hash for a user. If the tool already exists,
    /// updates the hash (which may revoke approval if it changed).
    /// </summary>
    public async Task RecordToolDefinitionAsync(
        Guid userId, string toolName, string definitionHash,
        CancellationToken cancellationToken = default)
    {
        var existing = await _unitOfWork.McpToolHashes
            .GetByUserAndToolAsync(userId, toolName, cancellationToken);

        if (existing is null)
        {
            var newHash = new McpToolHash(userId, toolName, definitionHash);
            await _unitOfWork.McpToolHashes.AddAsync(newHash, cancellationToken);
        }
        else
        {
            existing.UpdateHash(definitionHash);
            await _unitOfWork.McpToolHashes.UpdateAsync(existing, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Approves the current definition for a tool.
    /// </summary>
    public async Task ApproveToolAsync(
        Guid userId, string toolName,
        CancellationToken cancellationToken = default)
    {
        var existing = await _unitOfWork.McpToolHashes
            .GetByUserAndToolAsync(userId, toolName, cancellationToken);

        if (existing is null)
            throw new InvalidOperationException($"No tool hash record found for user '{userId}' and tool '{toolName}'.");

        existing.Approve();
        await _unitOfWork.McpToolHashes.UpdateAsync(existing, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Returns all tool hash records for a user.
    /// </summary>
    public async Task<IEnumerable<McpToolHash>> GetToolHashesAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.McpToolHashes.GetByUserAsync(userId, cancellationToken);
    }
}
