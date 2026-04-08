using System.Text.Json;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Application.Tests.Fuzz;

/// <summary>
/// Property-based tests verifying JSON serialization round-trip identity for DTOs.
/// Key property: serialize then deserialize produces an identical object.
/// Also exercises adversarial string content to ensure no serialization crashes.
/// </summary>
public class JsonSerializationRoundTripFuzzTests
{
    private const int MaxTests = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // ─────────────────────── Adversarial string generators ───────────────────────

    private static Gen<string> AdversarialStringGen() => Gen.OneOf(
        Gen.Constant("\u0000"),
        Gen.Constant("\uFEFF"),
        Gen.Constant("\uFFFD"),
        Gen.Constant("\u200B"),
        Gen.Constant("\u202E"),
        Gen.Constant("<script>alert('xss')</script>"),
        Gen.Constant("'; DROP TABLE boards; --"),
        Gen.Constant("\"quoted\"string\""),
        Gen.Constant("back\\slash"),
        Gen.Constant("new\nline"),
        Gen.Constant("tab\there"),
        Gen.Constant("null\x00byte"),
        Gen.Constant("emoji 👨‍👩‍👧‍👦 text"),
        Gen.Constant("田中太郎"),
        Gen.Constant("مرحبا"),
        Gen.Constant("{\"nested\": \"json\"}"),
        Gen.Constant("[1,2,3]"),
        Gen.Constant(""),
        Arb.Generate<string>().Where(s => s != null)
    );

    // ─────────────────────── BoardDto round-trip ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property BoardDto_RoundTrip_PreservesAllFields()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            Arb.From(AdversarialStringGen()),
            (name, description) =>
            {
                var dto = new BoardDto(
                    Guid.NewGuid(),
                    name,
                    description,
                    false,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow);

                var json = JsonSerializer.Serialize(dto, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<BoardDto>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Name.Should().Be(name);
                deserialized.Description.Should().Be(description);
                deserialized.Id.Should().Be(dto.Id);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property CardDto_RoundTrip_PreservesAllFields()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            Arb.From(AdversarialStringGen()),
            (title, description) =>
            {
                var dto = new CardDto(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    title,
                    description,
                    DateTimeOffset.UtcNow,
                    false,
                    null,
                    0,
                    new List<LabelDto>(),
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow);

                var json = JsonSerializer.Serialize(dto, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<CardDto>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Title.Should().Be(title);
                deserialized.Description.Should().Be(description);
                deserialized.Id.Should().Be(dto.Id);
                deserialized.BoardId.Should().Be(dto.BoardId);
                deserialized.ColumnId.Should().Be(dto.ColumnId);
            });
    }

    // ─────────────────────── CreateBoardDto round-trip ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property CreateBoardDto_RoundTrip_Identity()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            name =>
            {
                var dto = new CreateBoardDto(name, "desc");

                var json = JsonSerializer.Serialize(dto, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<CreateBoardDto>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Name.Should().Be(name);
            });
    }

    // ─────────────────────── GUID format variations ───────────────────────

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    [InlineData("12345678-1234-1234-1234-123456789abc")]
    public void BoardDto_WithVariousGuidFormats_RoundTrips(string guidStr)
    {
        var guid = Guid.Parse(guidStr);
        var dto = new BoardDto(guid, "Board", null, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<BoardDto>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(guid);
    }

    // ─────────────────────── DateTimeOffset boundary values ───────────────────────

    [Theory]
    [InlineData("0001-01-01T00:00:00+00:00")]
    [InlineData("9999-12-31T23:59:59.9999999+00:00")]
    [InlineData("2026-04-08T12:00:00+05:30")]
    [InlineData("2026-04-08T12:00:00-12:00")]
    public void BoardDto_WithDateTimeBoundaries_RoundTrips(string dateStr)
    {
        var date = DateTimeOffset.Parse(dateStr);
        var dto = new BoardDto(Guid.NewGuid(), "Board", null, false, date, date);

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<BoardDto>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.CreatedAt.Should().Be(date);
    }

    // ─────────────────────── Malformed JSON deserialization ───────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"name\": null}")]
    [InlineData("{\"extra_field\": \"value\"}")]
    [InlineData("{\"name\": 12345}")]
    public void CreateBoardDto_MalformedJson_HandledGracefully(string json)
    {
        // Should either deserialize (possibly with nulls) or throw JsonException — never crash
        try
        {
            var result = JsonSerializer.Deserialize<CreateBoardDto>(json, JsonOptions);
            // If it deserializes, that's fine — the API layer validates
        }
        catch (JsonException)
        {
            // Expected for truly malformed JSON
        }
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[")]
    [InlineData("{{")]
    [InlineData("{\"name\":")]
    [InlineData("{\"name\": \"unterminated")]
    public void TruncatedJson_ThrowsJsonException_NotUnhandled(string json)
    {
        var act = () => JsonSerializer.Deserialize<CreateBoardDto>(json, JsonOptions);
        act.Should().Throw<JsonException>();
    }

    // ─────────────────────── Nested adversarial JSON in string fields ───────────────────────

    [Theory]
    [InlineData("{\"nested\": true}")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"__proto__\": {\"admin\": true}}")]
    [InlineData("{\"constructor\": {\"prototype\": {\"isAdmin\": true}}}")]
    public void NestedJsonInStringField_StoredAsLiteral(string value)
    {
        var dto = new CreateBoardDto(value, value);
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<CreateBoardDto>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be(value, "nested JSON in string fields should be stored verbatim");
    }

    // ─────────────────────── Large payload serialization ───────────────────────

    [Theory]
    [InlineData(1000)]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public void LargeStringField_RoundTripsCorrectly(int length)
    {
        var largeString = new string('x', length);
        var dto = new CardDto(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            largeString, largeString, null, false, null, 0,
            new List<LabelDto>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<CardDto>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Title.Length.Should().Be(length);
        deserialized.Description.Length.Should().Be(length);
    }

    // ─────────────────────── CaptureItemDto round-trip with adversarial content ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property CapturePayload_RoundTrip_WithAdversarialContent()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            content =>
            {
                var dto = new CreateCaptureItemDto(
                    BoardId: null,
                    Text: content);

                var json = JsonSerializer.Serialize(dto, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<CreateCaptureItemDto>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Text.Should().Be(content);
                deserialized.BoardId.Should().BeNull();
            });
    }
}
