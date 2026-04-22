using FluentAssertions;
using Taskdeck.Cli.Commands;
using Xunit;

namespace Taskdeck.Cli.Tests;

public class CliActorIdentityTests
{
    [Fact]
    public void ActorUsername_IsNotEmpty()
    {
        CliActorIdentity.ActorUsername.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ActorEmail_UsesNonRoutableDomain()
    {
        CliActorIdentity.ActorEmail.Should().Contain("@system.taskdeck");
    }

    [Fact]
    public void ActorEmail_IsValidFormat()
    {
        CliActorIdentity.ActorEmail.Should().Contain("@");
        var parts = CliActorIdentity.ActorEmail.Split('@');
        parts.Should().HaveCount(2);
        parts[0].Should().NotBeNullOrWhiteSpace();
        parts[1].Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ActorUsername_MatchesExpectedValue()
    {
        // Guard against accidental renames that could break identity resolution
        CliActorIdentity.ActorUsername.Should().Be("taskdeck_cli_actor");
    }

    [Fact]
    public void ActorEmail_MatchesExpectedValue()
    {
        // Guard against accidental renames that could break identity resolution
        CliActorIdentity.ActorEmail.Should().Be("cli-actor@system.taskdeck");
    }
}
