namespace Taskdeck.Cli.Commands;

/// <summary>
/// Shared identity constants for the CLI system actor.
/// The email uses the non-routable <c>@system.taskdeck</c> domain, which cannot
/// be registered through the normal authentication flow (email uniqueness check
/// blocks it). This prevents identity hijacking where an attacker registers the
/// CLI username before the CLI creates its actor.
/// </summary>
internal static class CliActorIdentity
{
    /// <summary>Username for the CLI system actor.</summary>
    public const string ActorUsername = "taskdeck_cli_actor";

    /// <summary>
    /// Email for the CLI system actor. Uses a non-routable domain to prevent
    /// registration-based hijacking. Actor lookup uses email, not username.
    /// </summary>
    public const string ActorEmail = "cli-actor@system.taskdeck";
}
