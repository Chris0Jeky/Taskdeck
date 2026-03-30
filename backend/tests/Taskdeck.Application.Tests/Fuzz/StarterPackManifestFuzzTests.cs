using System.Text.Json;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;

namespace Taskdeck.Application.Tests.Fuzz;

/// <summary>
/// Fuzz-style tests for StarterPackManifestValidator.
/// These tests verify that the validator never throws unhandled exceptions
/// regardless of input, and that well-formed manifests always validate successfully.
/// Replay: set Replay = "seed,size" on any [Property] to reproduce a failing case.
/// </summary>
public class StarterPackManifestFuzzTests
{
    private const int MaxTests = 200;
    private readonly StarterPackManifestValidator _validator = new();

    [Property(MaxTest = MaxTests)]
    public Property ValidateJson_NeverThrows_OnArbitraryString()
    {
        return Prop.ForAll(
            Arb.From<string>(),
            input =>
            {
                // The validator should gracefully handle any string input
                // without throwing unhandled exceptions
                var act = () => _validator.ValidateJson(input);
                act.Should().NotThrow("ValidateJson must handle all inputs gracefully");
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ValidateJson_NeverThrows_OnMalformedJson()
    {
        return Prop.ForAll(
            MalformedJsonArb(),
            malformed =>
            {
                var act = () => _validator.ValidateJson(malformed);
                act.Should().NotThrow("ValidateJson must handle malformed JSON gracefully");
                var result = _validator.ValidateJson(malformed);
                result.IsValid.Should().BeFalse("malformed JSON should not validate");
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ValidateJson_EmptyAndWhitespace_ReturnError()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements("", " ", "\t", "\n", "\r\n", null!)),
            input =>
            {
                var result = _validator.ValidateJson(input);
                result.IsValid.Should().BeFalse();
                result.Errors.Should().NotBeEmpty();
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ValidateJson_ValidManifestJson_AlwaysSucceeds()
    {
        return Prop.ForAll(
            ValidManifestJsonArb(),
            json =>
            {
                var result = _validator.ValidateJson(json);
                result.IsValid.Should().BeTrue(
                    $"A well-formed manifest should validate successfully. Errors: " +
                    $"{string.Join("; ", result.Errors.Select(e => $"{e.Path}: {e.Message}"))}");
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property Validate_NullManifest_ReturnsError()
    {
        var result = _validator.Validate(null!);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        return true.ToProperty();
    }

    [Property(MaxTest = MaxTests)]
    public Property Validate_NeverThrows_OnRandomDto()
    {
        return Prop.ForAll(
            RandomManifestDtoArb(),
            dto =>
            {
                var act = () => _validator.Validate(dto);
                act.Should().NotThrow("Validate must handle all DTO shapes gracefully");
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ValidateJson_TruncatedJson_NeverThrows()
    {
        return Prop.ForAll(
            ValidManifestJsonArb().Generator
                .SelectMany(json =>
                    Gen.Choose(1, Math.Max(1, json.Length - 1))
                        .Select(cutoff => json[..cutoff]))
                .ToArbitrary(),
            truncated =>
            {
                var act = () => _validator.ValidateJson(truncated);
                act.Should().NotThrow("truncated JSON must be handled gracefully");
                var result = _validator.ValidateJson(truncated);
                result.IsValid.Should().BeFalse("truncated JSON should not validate");
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ValidateJson_NullInjectedFields_NeverThrows()
    {
        return Prop.ForAll(
            NullFieldManifestJsonArb(),
            json =>
            {
                var act = () => _validator.ValidateJson(json);
                act.Should().NotThrow("null-injected JSON must be handled gracefully");
            });
    }

    /// <summary>
    /// Generates malformed JSON strings that should fail parsing.
    /// </summary>
    private static Arbitrary<string> MalformedJsonArb()
    {
        return Arb.From(Gen.OneOf(
            Gen.Constant("{"),
            Gen.Constant("}"),
            Gen.Constant("["),
            Gen.Constant("{\"schemaVersion\":"),
            Gen.Constant("{\"schemaVersion\": \"1.0\", columns: invalid}"),
            Gen.Constant("not json at all"),
            Gen.Constant("<xml>not json</xml>"),
            Gen.Constant("{\"schemaVersion\": \"1.0\""),
            Gen.Constant("42"),
            Gen.Constant("true"),
            Gen.Constant("null"),
            Gen.Constant("\"just a string\""),
            Gen.Constant("{\"deeply\": {\"nested\": {\"but\": \"wrong\"}}}"),
            Gen.Constant("[]"),
            Gen.Constant("[1,2,3]")
        ));
    }

    /// <summary>
    /// Generates valid starter-pack manifest JSON.
    /// </summary>
    private static Arbitrary<string> ValidManifestJsonArb()
    {
        var gen = Gen.Choose(1, 5).SelectMany(labelCount =>
            Gen.Choose(1, 5).SelectMany(columnCount =>
            {
                var labels = Enumerable.Range(0, labelCount)
                    .Select(i => new StarterPackLabelDto
                    {
                        Name = $"Label{i}",
                        Color = $"#{i:D2}{i:D2}{i:D2}".Replace("00", "AA")
                    })
                    .ToList();

                // Fix label colors to valid hex
                for (var i = 0; i < labels.Count; i++)
                {
                    var hexByte = ((i + 1) * 37 % 256).ToString("X2");
                    labels[i].Color = $"#{hexByte}{hexByte}{hexByte}";
                }

                var columns = Enumerable.Range(0, columnCount)
                    .Select(i => new StarterPackColumnDto
                    {
                        Name = $"Column{i}",
                        Position = i,
                        WipLimit = i > 0 ? i * 5 : null
                    })
                    .ToList();

                var manifest = new StarterPackManifestDto
                {
                    SchemaVersion = "1.0",
                    PackId = "test-pack",
                    DisplayName = "Test Pack",
                    Description = "A test manifest",
                    Compatibility = new StarterPackCompatibilityDto
                    {
                        MinTaskdeckVersion = "0.1.0",
                        RequiredFeatures = new List<string>()
                    },
                    Tags = new List<string> { "test" },
                    Labels = labels,
                    Columns = columns,
                    Templates = new List<StarterPackCardTemplateDto>(),
                    SeedCards = new List<StarterPackSeedCardDto>()
                };

                return Gen.Constant(JsonSerializer.Serialize(manifest, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));
            }));

        return Arb.From(gen);
    }

    /// <summary>
    /// Generates random StarterPackManifestDto with arbitrary field values.
    /// </summary>
    private static Arbitrary<StarterPackManifestDto> RandomManifestDtoArb()
    {
        var gen = Arb.From<string>().Generator.SelectMany(schemaVer =>
            Arb.From<string>().Generator.SelectMany(packId =>
            Arb.From<string>().Generator.Select(displayName =>
            {
                var dto = new StarterPackManifestDto
                {
                    SchemaVersion = schemaVer ?? "",
                    PackId = packId ?? "",
                    DisplayName = displayName ?? "",
                    Description = null,
                    Compatibility = new StarterPackCompatibilityDto
                    {
                        MinTaskdeckVersion = schemaVer ?? "",
                        RequiredFeatures = new List<string>()
                    },
                    Tags = new List<string>(),
                    Labels = new List<StarterPackLabelDto>(),
                    Columns = new List<StarterPackColumnDto>(),
                    Templates = new List<StarterPackCardTemplateDto>(),
                    SeedCards = new List<StarterPackSeedCardDto>()
                };
                return dto;
            })));
        return Arb.From(gen);
    }

    /// <summary>
    /// Generates JSON with null-injected fields to test null-safety.
    /// </summary>
    private static Arbitrary<string> NullFieldManifestJsonArb()
    {
        return Arb.From(Gen.OneOf(
            Gen.Constant("{\"schemaVersion\":null,\"packId\":null,\"displayName\":null,\"compatibility\":null,\"tags\":null,\"labels\":null,\"columns\":null,\"templates\":null,\"seedCards\":null}"),
            Gen.Constant("{\"schemaVersion\":\"1.0\",\"packId\":\"test\",\"displayName\":\"Test\",\"compatibility\":null,\"tags\":[],\"labels\":[],\"columns\":[{\"name\":\"Col\",\"position\":0}],\"templates\":[],\"seedCards\":[]}"),
            Gen.Constant("{\"schemaVersion\":\"1.0\",\"packId\":\"test\",\"displayName\":null,\"tags\":[null],\"labels\":[null],\"columns\":[null],\"templates\":[null],\"seedCards\":[null]}"),
            Gen.Constant("{\"schemaVersion\":\"1.0\",\"packId\":\"test\",\"displayName\":\"Test\",\"compatibility\":{\"minTaskdeckVersion\":null,\"requiredFeatures\":null},\"tags\":[],\"labels\":[],\"columns\":[{\"name\":\"Col\",\"position\":0}],\"templates\":[],\"seedCards\":[]}")
        ));
    }
}
