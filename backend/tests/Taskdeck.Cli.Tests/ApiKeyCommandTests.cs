using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class ApiKeyCommandTests
{
    [Fact]
    public async Task ApiKeyCreate_ReturnsJsonWithTdskPrefix()
    {
        await using var harness = new CliTestHarness("cli-apikey");

        var result = await harness.RunAsync(
            "api-key create --name \"Test Key\" --scopes read,manage");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.GetProperty("key").GetString().Should().StartWith("tdsk_");
        doc.RootElement.GetProperty("name").GetString().Should().Be("Test Key");
        doc.RootElement.GetProperty("scopes").EnumerateArray()
            .Select(scope => scope.GetString())
            .Should().Equal("read", "manage");
        doc.RootElement.GetProperty("message").GetString().Should().Contain("cannot be retrieved");
    }

    [Fact]
    public async Task ApiKeyCreate_WithExpires_SetsExpiresAt()
    {
        await using var harness = new CliTestHarness("cli-apikey");

        var result = await harness.RunAsync(
            "api-key create --name \"Expiring\" --scopes propose --expires 90d");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.GetProperty("expiresAt").GetDateTimeOffset()
            .Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(90), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ApiKeyCreate_WithNumericExpires_AcceptsDaysWithoutSuffix()
    {
        await using var harness = new CliTestHarness("cli-apikey");

        var result = await harness.RunAsync(
            "api-key create --name \"NumExpiry\" --scopes manage --expires 30");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.GetProperty("expiresAt").GetDateTimeOffset()
            .Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ApiKeyCreate_MissingName_ReturnsUsageError()
    {
        await using var harness = new CliTestHarness("cli-apikey");

        var result = await harness.RunAsync("api-key create");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--name");
    }

    [Fact]
    public async Task ApiKeyCreate_InvalidExpires_ReturnsUsageError()
    {
        await using var harness = new CliTestHarness("cli-apikey");

        var result = await harness.RunAsync(
            "api-key create --name \"Bad\" --scopes read --expires abc");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Invalid --expires");
    }

    [Fact]
    public async Task ApiKeyList_ReturnsCreatedKeys()
    {
        await using var harness = new CliTestHarness("cli-apikey");

        await harness.RunAsync("api-key create --name \"List Key A\" --scopes read");
        await harness.RunAsync("api-key create --name \"List Key B\" --scopes propose,manage");

        var result = await harness.RunAsync("api-key list");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        var names = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .ToList();
        names.Should().Contain("List Key A");
        names.Should().Contain("List Key B");

        var listedScopes = doc.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("name").GetString() == "List Key B")
            .GetProperty("scopes")
            .EnumerateArray()
            .Select(scope => scope.GetString());
        listedScopes.Should().Equal("propose", "manage");
    }

    [Fact]
    public async Task ApiKeyList_ShowsKeyPrefixNotFullKey()
    {
        await using var harness = new CliTestHarness("cli-apikey");

        await harness.RunAsync("api-key create --name \"Prefix Key\" --scopes read");

        var result = await harness.RunAsync("api-key list");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var doc = JsonDocument.Parse(result.StdOut);
        var firstKey = doc.RootElement.EnumerateArray().First();
        firstKey.GetProperty("keyPrefix").GetString()!.Length.Should().Be(8);
        firstKey.GetProperty("keyPrefix").GetString().Should().StartWith("tdsk_");
    }

    [Fact]
    public async Task ApiKeyRevoke_ByName_SetsRevokedStatus()
    {
        await using var harness = new CliTestHarness("cli-apikey");

        await harness.RunAsync("api-key create --name \"Revoke Me\" --scopes read");

        var revokeResult = await harness.RunAsync("api-key revoke --name \"Revoke Me\"");
        revokeResult.ExitCode.Should().Be(0, revokeResult.StdErr);
        using var revokeDoc = JsonDocument.Parse(revokeResult.StdOut);
        revokeDoc.RootElement.GetProperty("status").GetString().Should().Be("ok");

        // Verify in list
        var listResult = await harness.RunAsync("api-key list");
        using var listDoc = JsonDocument.Parse(listResult.StdOut);
        var revokedKey = listDoc.RootElement.EnumerateArray()
            .First(e => e.GetProperty("name").GetString() == "Revoke Me");
        revokedKey.GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ApiKeyRevoke_ById_SetsRevokedStatus()
    {
        await using var harness = new CliTestHarness("cli-apikey");

        var createResult = await harness.RunAsync(
            "api-key create --name \"Revoke By Id\" --scopes manage");
        using var createDoc = JsonDocument.Parse(createResult.StdOut);
        var keyId = createDoc.RootElement.GetProperty("id").GetGuid();

        var revokeResult = await harness.RunAsync($"api-key revoke --id {keyId}");
        revokeResult.ExitCode.Should().Be(0, revokeResult.StdErr);
    }

    [Fact]
    public async Task ApiKeyRevoke_NonexistentName_ReturnsFailure()
    {
        await using var harness = new CliTestHarness("cli-apikey");

        var result = await harness.RunAsync("api-key revoke --name \"Does Not Exist\"");

        result.ExitCode.Should().Be(1);
        result.StdErr.Should().Contain("No active API key found");
    }

    [Fact]
    public async Task ApiKeyRevoke_MissingIdentifier_ReturnsUsageError()
    {
        await using var harness = new CliTestHarness("cli-apikey");

        var result = await harness.RunAsync("api-key revoke");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--name");
    }

    [Theory]
    [InlineData("api-key create --name \"Missing Scopes\"")]
    [InlineData("api-key create --name \"Empty Scopes\" --scopes \"\"")]
    [InlineData("api-key create --name \"None Scope\" --scopes none")]
    [InlineData("api-key create --name \"Full Alias\" --scopes full")]
    [InlineData("api-key create --name \"Unknown Scope\" --scopes read,unknown")]
    public async Task ApiKeyCreate_InvalidScopeSelection_ReturnsUsageError(string command)
    {
        await using var harness = new CliTestHarness("cli-apikey-invalid-scope");

        var result = await harness.RunAsync(command);

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("--scopes");

        var list = await harness.RunAsync("api-key list");
        using var doc = JsonDocument.Parse(list.StdOut);
        doc.RootElement.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task ApiKey_UnknownSubcommand_ReturnsUsageError()
    {
        await using var harness = new CliTestHarness("cli-apikey");

        var result = await harness.RunAsync("api-key unknown");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Unknown api-key command");
    }
}
