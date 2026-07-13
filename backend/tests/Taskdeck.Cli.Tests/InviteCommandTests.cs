using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Cli.Tests;

public sealed class InviteCommandTests
{
    [Fact]
    public async Task InviteCreate_ReturnsPlaintextOnceWithoutCreatingAUserOrClaimingBootstrap()
    {
        await using var harness = new CliTestHarness("cli-invite");

        var result = await harness.RunAsync("invite create --expires 2");

        result.ExitCode.Should().Be(0, result.StdErr);
        using var document = JsonDocument.Parse(result.StdOut);
        var code = document.RootElement.GetProperty("code").GetString();
        code.Should().NotBeNull();
        code.Should().StartWith(RegistrationInvite.CodePrefix);
        code.Should().HaveLength(RegistrationInvite.RawCodeLength);
        document.RootElement.GetProperty("message").GetString()
            .Should()
            .Contain("cannot be retrieved again");

        await using var connection = new SqliteConnection($"Data Source={harness.DatabasePath}");
        await connection.OpenAsync();
        (await ExecuteScalarAsync<long>(connection, "SELECT COUNT(*) FROM Users"))
            .Should()
            .Be(0);
        (await ExecuteScalarAsync<long>(connection, "SELECT COUNT(*) FROM RegistrationBootstraps"))
            .Should()
            .Be(0);
        (await ExecuteScalarAsync<long>(connection, "SELECT COUNT(*) FROM RegistrationInvites"))
            .Should()
            .Be(1);
        (await ExecuteScalarAsync<string>(connection, "SELECT CodeHash FROM RegistrationInvites"))
            .Should()
            .Be(RegistrationPolicyService.HashInviteCode(code!));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("366")]
    [InlineData("not-days")]
    public async Task InviteCreate_RejectsInvalidExpiration(string expiration)
    {
        await using var harness = new CliTestHarness("cli-invite-invalid");

        var result = await harness.RunAsync($"invite create --expires {expiration}");

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Invalid --expires value");
    }

    private static async Task<T> ExecuteScalarAsync<T>(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value!, typeof(T));
    }
}
