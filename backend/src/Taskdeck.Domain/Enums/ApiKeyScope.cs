namespace Taskdeck.Domain.Enums;

/// <summary>
/// Defines the capabilities granted to an API key.
/// </summary>
[Flags]
public enum ApiKeyScope
{
    None = 0,
    Read = 1,
    Propose = 2,
    Manage = 4,
    Full = Read | Propose | Manage
}
