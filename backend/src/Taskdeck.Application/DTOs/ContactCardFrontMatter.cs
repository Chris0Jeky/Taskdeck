namespace Taskdeck.Application.DTOs;

/// <summary>
/// Represents the structured contact fields stored as YAML front matter
/// in a card description for the card-first Outreach CRM.
/// </summary>
public sealed class ContactCardFrontMatter
{
    /// <summary>Card type discriminator (always "contact").</summary>
    public string Type { get; set; } = "contact";

    /// <summary>Contact display name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Relationship tier: A, B, or C.</summary>
    public string? RelationshipTier { get; set; }

    /// <summary>Company or organisation name.</summary>
    public string? Company { get; set; }

    /// <summary>Job title or role.</summary>
    public string? Role { get; set; }

    /// <summary>IANA timezone identifier (e.g. "Europe/London").</summary>
    public string? LocationTz { get; set; }

    /// <summary>Contact handles keyed by platform (e.g. linkedin_url, github, email).</summary>
    public Dictionary<string, string>? Handles { get; set; }

    /// <summary>Freeform tags for filtering and grouping.</summary>
    public List<string>? Tags { get; set; }

    /// <summary>How you originally met or discovered this contact.</summary>
    public string? Source { get; set; }

    /// <summary>Relationship status: cold, warm, active, referral, interviewing, closed.</summary>
    public string? Status { get; set; }

    /// <summary>Cadence template identifier (e.g. "warm-3-7-21").</summary>
    public string? CadenceId { get; set; }

    /// <summary>Date of last interaction (ISO 8601 date string).</summary>
    public string? LastTouchAt { get; set; }

    /// <summary>Date of next planned interaction (ISO 8601 date string).</summary>
    public string? NextTouchAt { get; set; }

    /// <summary>Private notes about the contact.</summary>
    public string? NotesPrivate { get; set; }
}
