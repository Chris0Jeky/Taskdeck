namespace Taskdeck.Domain.Entities;

/// <summary>
/// Singleton database claim that makes the first-user bootstrap atomic.
/// Existing installations receive the claim during migration; a fresh
/// installation claims it in the same transaction that creates its first user.
/// </summary>
public sealed class RegistrationBootstrap
{
    public const string SingletonId = "registration";

    public string Id { get; private set; } = SingletonId;
    public DateTimeOffset ClaimedAt { get; private set; }

    private RegistrationBootstrap()
    {
    }
}
