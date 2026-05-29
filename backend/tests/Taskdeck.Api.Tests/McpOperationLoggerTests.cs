using FluentAssertions;
using Microsoft.Extensions.Logging;
using Taskdeck.Api.Mcp;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Tests for <see cref="McpOperationLogger"/> and <see cref="McpOperationScope"/>.
/// Verifies structured log properties, sensitive data protection, and telemetry safety.
/// </summary>
public class McpOperationLoggerTests
{
    private readonly InMemoryLogger<McpOperationLogger> _logger = new();

    [Fact]
    public void BeginOperation_LogsStartMessage()
    {
        var operationLogger = new McpOperationLogger(_logger);
        var userId = Guid.NewGuid();

        using var scope = operationLogger.BeginOperation("tool", "search_cards", userId, "http");

        _logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("search_cards") &&
            e.Message.Contains("tool") &&
            e.Message.Contains("started"));
    }

    [Fact]
    public void BeginOperation_IncludesUserId()
    {
        var operationLogger = new McpOperationLogger(_logger);
        var userId = Guid.NewGuid();

        using var scope = operationLogger.BeginOperation("tool", "create_card", userId);

        _logger.Entries.Should().ContainSingle(e => e.Message.Contains(userId.ToString()));
    }

    [Fact]
    public void BeginOperation_IncludesTransport()
    {
        var operationLogger = new McpOperationLogger(_logger);

        using var scope = operationLogger.BeginOperation("resource", "boards", transport: "stdio");

        _logger.Entries.Should().ContainSingle(e => e.Message.Contains("stdio"));
    }

    [Fact]
    public void Complete_LogsCompletionMessage()
    {
        var operationLogger = new McpOperationLogger(_logger);

        using var scope = operationLogger.BeginOperation("tool", "search_cards");
        scope.Complete();

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("completed") &&
            e.Message.Contains("DurationMs"));
    }

    [Fact]
    public void Fail_WithException_LogsErrorMessage()
    {
        var operationLogger = new McpOperationLogger(_logger);
        var exception = new InvalidOperationException("board not found");

        using var scope = operationLogger.BeginOperation("tool", "get_board_summary");
        scope.Fail(exception);

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains("failed") &&
            e.Message.Contains("InvalidOperationException") &&
            e.Exception == exception);
    }

    [Fact]
    public void Fail_WithString_LogsWarningMessage()
    {
        var operationLogger = new McpOperationLogger(_logger);

        using var scope = operationLogger.BeginOperation("tool", "create_card");
        scope.Fail("Invalid board_id format");

        _logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("failed") &&
            e.Message.Contains("Invalid board_id format"));
    }

    [Fact]
    public void Complete_CalledTwice_OnlyLogsOnce()
    {
        var operationLogger = new McpOperationLogger(_logger);

        using var scope = operationLogger.BeginOperation("tool", "search_cards");
        scope.Complete();
        scope.Complete(); // second call

        var completionEntries = _logger.Entries
            .Where(e => e.Message.Contains("completed"))
            .ToList();

        completionEntries.Should().HaveCount(1, "double-complete should be idempotent");
    }

    [Fact]
    public void Dispose_WithoutExplicitComplete_RecordsCompletion()
    {
        var operationLogger = new McpOperationLogger(_logger);

        var scope = operationLogger.BeginOperation("resource", "boards");
        scope.Dispose();

        _logger.Entries.Should().Contain(e => e.Message.Contains("completed"),
            "disposing without explicit Complete/Fail should default to completion");
    }

    [Fact]
    public void Arguments_LoggedOnlyAtDebugLevel()
    {
        var operationLogger = new McpOperationLogger(_logger);
        var args = "{\"board_id\": \"abc\", \"title\": \"My Task\"}";

        using var scope = operationLogger.BeginOperation(
            "tool", "create_card", arguments: args);

        // InMemoryLogger has IsEnabled=true for all levels, so Debug logs appear.
        var debugEntries = _logger.Entries.Where(e => e.Level == LogLevel.Debug).ToList();
        debugEntries.Should().ContainSingle(e => e.Message.Contains("arguments"));

        // The args should NOT appear in any Information-level entries.
        var infoEntries = _logger.Entries.Where(e => e.Level == LogLevel.Information).ToList();
        infoEntries.Should().NotContain(e => e.Message.Contains(args));
    }

    [Fact]
    public void Arguments_TruncatedWhenTooLong()
    {
        var operationLogger = new McpOperationLogger(_logger);
        var longArgs = new string('x', McpOperationLogger.MaxArgumentLogLength + 100);

        using var scope = operationLogger.BeginOperation(
            "tool", "create_card", arguments: longArgs);

        var debugEntry = _logger.Entries.Single(e => e.Level == LogLevel.Debug);
        debugEntry.Message.Should().Contain("[truncated]");
        debugEntry.Message.Should().NotContain(longArgs,
            "full long arguments must be truncated");
    }

    [Fact]
    public void Arguments_NullArguments_NoDebugLog()
    {
        var operationLogger = new McpOperationLogger(_logger);

        using var scope = operationLogger.BeginOperation("tool", "search_cards", arguments: null);

        _logger.Entries.Where(e => e.Level == LogLevel.Debug).Should().BeEmpty();
    }

    [Fact]
    public void NeverLogsApiKeyValues()
    {
        var operationLogger = new McpOperationLogger(_logger);
        var argsWithKey = "{\"auth\": \"tdsk_secretkey12345678\"}";

        // Arguments with embedded API keys should only appear at Debug level,
        // and production loggers should have Debug disabled in production.
        // Here we verify the key is NOT logged at Info/Warning/Error level.
        using var scope = operationLogger.BeginOperation(
            "tool", "create_card",
            userId: Guid.NewGuid(),
            arguments: argsWithKey);
        scope.Complete();

        var nonDebugEntries = _logger.Entries
            .Where(e => e.Level != LogLevel.Debug)
            .ToList();

        foreach (var entry in nonDebugEntries)
        {
            entry.Message.Should().NotContain("tdsk_",
                "API key values must never appear in non-Debug log entries");
        }
    }

    [Fact]
    public void NullUserId_DoesNotThrow()
    {
        var operationLogger = new McpOperationLogger(_logger);

        using var scope = operationLogger.BeginOperation("tool", "search_cards", userId: null);
        scope.Complete();

        // Should not throw, and should still log
        _logger.Entries.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void ResourceOperation_LogsCorrectType()
    {
        var operationLogger = new McpOperationLogger(_logger);

        using var scope = operationLogger.BeginOperation("resource", "board_detail");
        scope.Complete();

        _logger.Entries.Should().Contain(e => e.Message.Contains("resource"));
        _logger.Entries.Should().Contain(e => e.Message.Contains("board_detail"));
    }

    [Fact]
    public void Fail_WithException_ThenDispose_DoesNotOverwriteFailure()
    {
        var operationLogger = new McpOperationLogger(_logger);
        var exception = new InvalidOperationException("test error");

        var scope = operationLogger.BeginOperation("tool", "search_cards");
        scope.Fail(exception);
        scope.Dispose(); // should not record a second "completed" entry

        var failEntries = _logger.Entries
            .Where(e => e.Message.Contains("failed"))
            .ToList();
        var completionEntries = _logger.Entries
            .Where(e => e.Message.Contains("completed"))
            .ToList();

        failEntries.Should().HaveCount(1, "Fail should be recorded once");
        completionEntries.Should().BeEmpty(
            "Dispose after Fail should not overwrite the failure with a completion");
    }

    [Fact]
    public void Fail_WithString_ThenDispose_DoesNotOverwriteFailure()
    {
        var operationLogger = new McpOperationLogger(_logger);

        var scope = operationLogger.BeginOperation("tool", "create_card");
        scope.Fail("validation error");
        scope.Dispose(); // should not record a second "completed" entry

        var failEntries = _logger.Entries
            .Where(e => e.Message.Contains("failed"))
            .ToList();
        var completionEntries = _logger.Entries
            .Where(e => e.Message.Contains("completed"))
            .ToList();

        failEntries.Should().HaveCount(1, "Fail(string) should be recorded once");
        completionEntries.Should().BeEmpty(
            "Dispose after Fail(string) should not overwrite the failure with a completion");
    }
}
