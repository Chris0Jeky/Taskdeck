using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class McpToolDefinitionHashServiceTests
{
    [Fact]
    public void ComputeDefinitionHash_DeterministicForSameInput()
    {
        var hash1 = McpToolDefinitionHashService.ComputeDefinitionHash(
            "test_tool", "Description", "{\"type\":\"object\"}");
        var hash2 = McpToolDefinitionHashService.ComputeDefinitionHash(
            "test_tool", "Description", "{\"type\":\"object\"}");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeDefinitionHash_DifferentForDifferentName()
    {
        var hash1 = McpToolDefinitionHashService.ComputeDefinitionHash(
            "tool_a", "Description", "{\"type\":\"object\"}");
        var hash2 = McpToolDefinitionHashService.ComputeDefinitionHash(
            "tool_b", "Description", "{\"type\":\"object\"}");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeDefinitionHash_DifferentForDifferentDescription()
    {
        var hash1 = McpToolDefinitionHashService.ComputeDefinitionHash(
            "tool", "Description A", "{\"type\":\"object\"}");
        var hash2 = McpToolDefinitionHashService.ComputeDefinitionHash(
            "tool", "Description B", "{\"type\":\"object\"}");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeDefinitionHash_DifferentForDifferentSchema()
    {
        var hash1 = McpToolDefinitionHashService.ComputeDefinitionHash(
            "tool", "Description", "{\"type\":\"object\"}");
        var hash2 = McpToolDefinitionHashService.ComputeDefinitionHash(
            "tool", "Description", "{\"type\":\"string\"}");

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeDefinitionHash_ReturnsLowercaseHex()
    {
        var hash = McpToolDefinitionHashService.ComputeDefinitionHash(
            "tool", "desc", "schema");

        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputeDefinitionHash_Has64CharLength()
    {
        var hash = McpToolDefinitionHashService.ComputeDefinitionHash(
            "tool", "desc", "schema");

        hash.Should().HaveLength(64); // SHA-256 = 256 bits = 64 hex chars
    }
}
