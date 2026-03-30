using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ContactCardYamlParserTests
{
    // ── Round-trip ──────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_FullContact_ProducesIdenticalOutput()
    {
        var original = new ContactCardFrontMatter
        {
            Type = "contact",
            DisplayName = "Jane Doe",
            RelationshipTier = "A",
            Company = "Google",
            Role = "SRE",
            LocationTz = "Europe/London",
            Handles = new Dictionary<string, string>
            {
                ["linkedin_url"] = "https://www.linkedin.com/in/jane-doe/",
                ["github"] = "janedoe",
                ["email"] = "jane@example.com"
            },
            Tags = new List<string> { "google", "platform", "referral-target" },
            Source = "GE colleague",
            Status = "warm",
            CadenceId = "warm-3-7-21",
            LastTouchAt = "2026-02-20",
            NextTouchAt = "2026-02-27",
            NotesPrivate = "Met at X; cares about reliability; likes concise messages."
        };

        var body = "## Timeline\n- 2026-02-20 (LI DM, outbound): Asked for feedback.";
        var serialized = ContactCardYamlParser.Serialize(original, body);
        var result = ContactCardYamlParser.Parse(serialized);

        result.Errors.Should().BeEmpty();
        result.FrontMatter.Should().NotBeNull();
        result.FrontMatter!.Type.Should().Be("contact");
        result.FrontMatter.DisplayName.Should().Be("Jane Doe");
        result.FrontMatter.RelationshipTier.Should().Be("A");
        result.FrontMatter.Company.Should().Be("Google");
        result.FrontMatter.Role.Should().Be("SRE");
        result.FrontMatter.LocationTz.Should().Be("Europe/London");
        result.FrontMatter.Handles.Should().ContainKey("email").WhoseValue.Should().Be("jane@example.com");
        result.FrontMatter.Tags.Should().BeEquivalentTo(new[] { "google", "platform", "referral-target" });
        result.FrontMatter.Source.Should().Be("GE colleague");
        result.FrontMatter.Status.Should().Be("warm");
        result.FrontMatter.CadenceId.Should().Be("warm-3-7-21");
        result.FrontMatter.LastTouchAt.Should().Be("2026-02-20");
        result.FrontMatter.NextTouchAt.Should().Be("2026-02-27");
        result.FrontMatter.NotesPrivate.Should().Be("Met at X; cares about reliability; likes concise messages.");
        result.Body.Should().Be(body);

        // Re-serialize and compare to ensure stability.
        var reSerialized = ContactCardYamlParser.Serialize(result.FrontMatter, result.Body);
        reSerialized.Should().Be(serialized);
    }

    [Fact]
    public void RoundTrip_MinimalContact_PreservesFields()
    {
        var original = new ContactCardFrontMatter
        {
            DisplayName = "Minimal User"
        };

        var serialized = ContactCardYamlParser.Serialize(original);
        var result = ContactCardYamlParser.Parse(serialized);

        result.Errors.Should().BeEmpty();
        result.FrontMatter.Should().NotBeNull();
        result.FrontMatter!.DisplayName.Should().Be("Minimal User");
        result.FrontMatter.Type.Should().Be("contact");
        result.Body.Should().BeEmpty();
    }

    // ── Parse: no front matter ─────────────────────────────────────

    [Fact]
    public void Parse_NullDescription_ReturnsNullFrontMatterAndEmptyBody()
    {
        var result = ContactCardYamlParser.Parse(null);

        result.FrontMatter.Should().BeNull();
        result.Body.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_EmptyDescription_ReturnsNullFrontMatterAndEmptyBody()
    {
        var result = ContactCardYamlParser.Parse(string.Empty);

        result.FrontMatter.Should().BeNull();
        result.Body.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_PlainTextWithoutDelimiters_ReturnsBodyOnly()
    {
        var text = "Just some notes about a contact.\nNo YAML here.";
        var result = ContactCardYamlParser.Parse(text);

        result.FrontMatter.Should().BeNull();
        result.Body.Should().Be(text);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_DelimitersNotAtStart_TreatsAsPlainText()
    {
        var text = "Some preamble\n---\ntype: contact\n---\nBody";
        var result = ContactCardYamlParser.Parse(text);

        result.FrontMatter.Should().BeNull();
        result.Body.Should().Be(text);
        result.Errors.Should().BeEmpty();
    }

    // ── Parse: malformed YAML ──────────────────────────────────────

    [Fact]
    public void Parse_OpeningDelimiterWithoutClosing_ReturnsError()
    {
        var text = "---\ntype: contact\ndisplay_name: Jane";
        var result = ContactCardYamlParser.Parse(text);

        result.FrontMatter.Should().BeNull();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("no closing '---'");
    }

    [Fact]
    public void Parse_InvalidYamlSyntax_ReturnsError()
    {
        var text = "---\ntype: contact\n  bad indent: [unclosed\n---\n";
        var result = ContactCardYamlParser.Parse(text);

        result.FrontMatter.Should().BeNull();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("Invalid YAML");
    }

    [Fact]
    public void Parse_EmptyFrontMatterBlock_ReturnsError()
    {
        var text = "---\n---\nSome body text.";
        var result = ContactCardYamlParser.Parse(text);

        result.FrontMatter.Should().BeNull();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("empty");
    }

    // ── Parse: validation errors ───────────────────────────────────

    [Fact]
    public void Parse_InvalidRelationshipTier_ReturnsValidationError()
    {
        var text = "---\ntype: contact\ndisplay_name: Jane\nrelationship_tier: X\n---\n";
        var result = ContactCardYamlParser.Parse(text);

        result.FrontMatter.Should().NotBeNull();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("relationship_tier");
    }

    [Fact]
    public void Parse_InvalidStatus_ReturnsValidationError()
    {
        var text = "---\ntype: contact\nstatus: unknown\n---\n";
        var result = ContactCardYamlParser.Parse(text);

        result.FrontMatter.Should().NotBeNull();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("status");
    }

    [Fact]
    public void Parse_InvalidType_ReturnsValidationError()
    {
        var text = "---\ntype: task\n---\n";
        var result = ContactCardYamlParser.Parse(text);

        result.FrontMatter.Should().NotBeNull();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("Unsupported front matter type");
    }

    [Fact]
    public void Parse_MultipleValidationErrors_ReturnsAll()
    {
        var text = "---\ntype: task\nrelationship_tier: Z\nstatus: bogus\n---\n";
        var result = ContactCardYamlParser.Parse(text);

        result.FrontMatter.Should().NotBeNull();
        result.Errors.Should().HaveCount(3);
    }

    // ── Parse: edge cases ──────────────────────────────────────────

    [Fact]
    public void Parse_WindowsLineEndings_HandledCorrectly()
    {
        var text = "---\r\ntype: contact\r\ndisplay_name: Jane\r\n---\r\nBody content.";
        var result = ContactCardYamlParser.Parse(text);

        result.Errors.Should().BeEmpty();
        result.FrontMatter.Should().NotBeNull();
        result.FrontMatter!.DisplayName.Should().Be("Jane");
        result.Body.Should().Be("Body content.");
    }

    [Fact]
    public void Parse_UnicodeDisplayName_Preserved()
    {
        var text = "---\ntype: contact\ndisplay_name: \"Müller Straße 日本語\"\n---\n";
        var result = ContactCardYamlParser.Parse(text);

        result.Errors.Should().BeEmpty();
        result.FrontMatter.Should().NotBeNull();
        result.FrontMatter!.DisplayName.Should().Be("Müller Straße 日本語");
    }

    [Fact]
    public void Parse_SpecialCharactersInNotesPrivate_Preserved()
    {
        var text = "---\ntype: contact\nnotes_private: \"Contains: colons, #hashes, @mentions & ampersands\"\n---\n";
        var result = ContactCardYamlParser.Parse(text);

        result.Errors.Should().BeEmpty();
        result.FrontMatter!.NotesPrivate.Should().Be("Contains: colons, #hashes, @mentions & ampersands");
    }

    [Fact]
    public void Parse_QuotedYamlValues_HandledCorrectly()
    {
        var text = "---\ntype: contact\ndisplay_name: \"Doe, Jane\"\ncompany: \"O'Reilly Media\"\n---\n";
        var result = ContactCardYamlParser.Parse(text);

        result.Errors.Should().BeEmpty();
        result.FrontMatter!.DisplayName.Should().Be("Doe, Jane");
        result.FrontMatter.Company.Should().Be("O'Reilly Media");
    }

    [Fact]
    public void Parse_EmptyTags_ResultsInEmptyList()
    {
        var text = "---\ntype: contact\ntags: []\n---\n";
        var result = ContactCardYamlParser.Parse(text);

        result.Errors.Should().BeEmpty();
        result.FrontMatter!.Tags.Should().BeEmpty();
    }

    [Fact]
    public void Parse_EmptyHandles_ResultsInEmptyDictionary()
    {
        var text = "---\ntype: contact\nhandles: {}\n---\n";
        var result = ContactCardYamlParser.Parse(text);

        result.Errors.Should().BeEmpty();
        result.FrontMatter!.Handles.Should().BeEmpty();
    }

    [Fact]
    public void Parse_UnknownFields_Ignored()
    {
        var text = "---\ntype: contact\ndisplay_name: Jane\ncustom_field: some_value\n---\n";
        var result = ContactCardYamlParser.Parse(text);

        result.Errors.Should().BeEmpty();
        result.FrontMatter.Should().NotBeNull();
        result.FrontMatter!.DisplayName.Should().Be("Jane");
    }

    [Fact]
    public void Parse_TrailingWhitespaceOnDelimiters_StillDetected()
    {
        var text = "---  \ntype: contact\ndisplay_name: Jane\n---  \nBody.";
        var result = ContactCardYamlParser.Parse(text);

        result.Errors.Should().BeEmpty();
        result.FrontMatter!.DisplayName.Should().Be("Jane");
        result.Body.Should().Be("Body.");
    }

    [Fact]
    public void Parse_BodyWithMultipleDelimiterLikeLines_OnlyFirstClosingUsed()
    {
        var text = "---\ntype: contact\n---\n---\nMore body\n---";
        var result = ContactCardYamlParser.Parse(text);

        result.Errors.Should().BeEmpty();
        result.FrontMatter.Should().NotBeNull();
        result.Body.Should().Be("---\nMore body\n---");
    }

    [Fact]
    public void Parse_NoBodyAfterClosingDelimiter_ReturnsEmptyBody()
    {
        var text = "---\ntype: contact\ndisplay_name: Jane\n---";
        var result = ContactCardYamlParser.Parse(text);

        result.Errors.Should().BeEmpty();
        result.FrontMatter!.DisplayName.Should().Be("Jane");
        result.Body.Should().BeEmpty();
    }

    // ── Serialize ──────────────────────────────────────────────────

    [Fact]
    public void Serialize_NullFrontMatter_ThrowsArgumentNullException()
    {
        var act = () => ContactCardYamlParser.Serialize(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Serialize_NullBody_OmitsBodySection()
    {
        var fm = new ContactCardFrontMatter { DisplayName = "Jane" };
        var result = ContactCardYamlParser.Serialize(fm, null);

        result.Should().StartWith("---\n");
        result.Should().EndWith("\n---");
        result.Split('\n').Count(l => l.TrimEnd() == "---").Should().Be(2);
    }

    [Fact]
    public void Serialize_EmptyBody_OmitsBodySection()
    {
        var fm = new ContactCardFrontMatter { DisplayName = "Jane" };
        var result = ContactCardYamlParser.Serialize(fm, string.Empty);

        result.Should().EndWith("\n---");
    }

    [Fact]
    public void Serialize_OmitsNullFields()
    {
        var fm = new ContactCardFrontMatter
        {
            DisplayName = "Jane",
            Company = null,
            Role = null
        };

        var result = ContactCardYamlParser.Serialize(fm);

        result.Should().NotContain("company:");
        result.Should().NotContain("role:");
        result.Should().Contain("display_name: Jane");
    }

    [Fact]
    public void Serialize_UsesUnderscoreNamingConvention()
    {
        var fm = new ContactCardFrontMatter
        {
            DisplayName = "Jane",
            RelationshipTier = "A",
            LocationTz = "US/Pacific",
            CadenceId = "warm-3-7-21",
            LastTouchAt = "2026-02-20",
            NextTouchAt = "2026-02-27",
            NotesPrivate = "Private note"
        };

        var result = ContactCardYamlParser.Serialize(fm);

        result.Should().Contain("display_name:");
        result.Should().Contain("relationship_tier:");
        result.Should().Contain("location_tz:");
        result.Should().Contain("cadence_id:");
        result.Should().Contain("last_touch_at:");
        result.Should().Contain("next_touch_at:");
        result.Should().Contain("notes_private:");
    }

    // ── Validate (internal) ────────────────────────────────────────

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("C")]
    public void Validate_ValidRelationshipTier_NoErrors(string tier)
    {
        var fm = new ContactCardFrontMatter { RelationshipTier = tier };
        var errors = ContactCardYamlParser.Validate(fm);
        errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("cold")]
    [InlineData("warm")]
    [InlineData("active")]
    [InlineData("referral")]
    [InlineData("interviewing")]
    [InlineData("closed")]
    public void Validate_ValidStatus_NoErrors(string status)
    {
        var fm = new ContactCardFrontMatter { Status = status };
        var errors = ContactCardYamlParser.Validate(fm);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_NullOptionalFields_NoErrors()
    {
        var fm = new ContactCardFrontMatter();
        var errors = ContactCardYamlParser.Validate(fm);
        errors.Should().BeEmpty();
    }
}
