namespace Taskdeck.Application.Services;

/// <summary>
/// Classifies the sensitivity of data sent to an external host.
/// Used by the egress registry to document what category of data
/// each outbound connection carries.
/// </summary>
public enum EgressDataClassification
{
    /// <summary>No user data is transmitted.</summary>
    None = 0,

    /// <summary>Only metadata (counts, timestamps, status codes) -- no user content.</summary>
    MetadataOnly = 1,

    /// <summary>User-created content (task text, card descriptions, chat messages).</summary>
    UserContent = 2,

    /// <summary>Credentials, API keys, or authentication tokens.</summary>
    Credentials = 3,
}
