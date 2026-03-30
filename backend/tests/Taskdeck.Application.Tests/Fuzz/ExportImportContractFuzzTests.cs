using System.Text.Json;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Tests.Fuzz;

/// <summary>
/// Fuzz-style tests for export/import DTO serialization contracts.
/// Verifies JSON roundtrip fidelity and that deserialization of arbitrary payloads
/// never throws unhandled exceptions.
/// Replay: set Replay = "seed,size" on any [Property] to reproduce a failing case.
/// </summary>
public class ExportImportContractFuzzTests
{
    private const int MaxTests = 200;

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [Property(MaxTest = MaxTests)]
    public Property ImportBoardDto_Roundtrip_PreservesData()
    {
        return Prop.ForAll(
            ValidImportBoardDtoArb(),
            dto =>
            {
                var json = JsonSerializer.Serialize(dto, CamelCaseOptions);
                var deserialized = JsonSerializer.Deserialize<ImportBoardDto>(json, CamelCaseOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Name.Should().Be(dto.Name);
                deserialized.Description.Should().Be(dto.Description);
                deserialized.Columns.Count().Should().Be(dto.Columns.Count());
                deserialized.Cards.Count().Should().Be(dto.Cards.Count());
                deserialized.Labels.Count().Should().Be(dto.Labels.Count());
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ImportBoardDto_Deserialize_NeverThrows_OnArbitraryJson()
    {
        return Prop.ForAll(
            Arb.From<string>(),
            json =>
            {
                // Deserialization of arbitrary strings should either succeed or throw JsonException,
                // never an unhandled exception type
                try
                {
                    JsonSerializer.Deserialize<ImportBoardDto>(json ?? "null", CamelCaseOptions);
                }
                catch (JsonException)
                {
                    // Expected for malformed JSON
                }
                catch (Exception ex)
                {
                    // Any other exception type is a test failure
                    ex.Should().BeNull($"Unexpected exception type {ex.GetType()}: {ex.Message}");
                }
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ImportColumnDto_Roundtrip_PreservesData()
    {
        return Prop.ForAll(
            ValidImportColumnDtoArb(),
            dto =>
            {
                var json = JsonSerializer.Serialize(dto, CamelCaseOptions);
                var deserialized = JsonSerializer.Deserialize<ImportColumnDto>(json, CamelCaseOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Name.Should().Be(dto.Name);
                deserialized.Position.Should().Be(dto.Position);
                deserialized.WipLimit.Should().Be(dto.WipLimit);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ImportCardDto_Roundtrip_PreservesData()
    {
        return Prop.ForAll(
            ValidImportCardDtoArb(),
            dto =>
            {
                var json = JsonSerializer.Serialize(dto, CamelCaseOptions);
                var deserialized = JsonSerializer.Deserialize<ImportCardDto>(json, CamelCaseOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Title.Should().Be(dto.Title);
                deserialized.Description.Should().Be(dto.Description);
                deserialized.ColumnName.Should().Be(dto.ColumnName);
                deserialized.Position.Should().Be(dto.Position);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ImportLabelDto_Roundtrip_PreservesData()
    {
        return Prop.ForAll(
            ValidImportLabelDtoArb(),
            dto =>
            {
                var json = JsonSerializer.Serialize(dto, CamelCaseOptions);
                var deserialized = JsonSerializer.Deserialize<ImportLabelDto>(json, CamelCaseOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Name.Should().Be(dto.Name);
                deserialized.Color.Should().Be(dto.Color);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ImportBoardDto_ExtraFields_IgnoredGracefully()
    {
        // Verify that extra/unknown fields in JSON don't break deserialization
        return Prop.ForAll(
            Arb.From(Gen.OneOf(
                Gen.Constant("{\"name\":\"Test\",\"unknownField\":42,\"columns\":[],\"cards\":[],\"labels\":[]}"),
                Gen.Constant("{\"name\":\"Test\",\"description\":\"Desc\",\"extra\":{\"nested\":true},\"columns\":[],\"cards\":[],\"labels\":[]}"),
                Gen.Constant("{\"name\":\"Test\",\"columns\":[{\"name\":\"Col\",\"position\":0,\"extra\":true}],\"cards\":[],\"labels\":[]}")
            )),
            json =>
            {
                var act = () => JsonSerializer.Deserialize<ImportBoardDto>(json, CamelCaseOptions);
                act.Should().NotThrow("extra fields should be silently ignored");
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ImportBoardDto_MissingFields_HandledGracefully()
    {
        return Prop.ForAll(
            Arb.From(Gen.OneOf(
                Gen.Constant("{}"),
                Gen.Constant("{\"name\":null}"),
                Gen.Constant("{\"name\":\"Test\"}"),
                Gen.Constant("{\"columns\":[]}"),
                Gen.Constant("{\"name\":\"\",\"columns\":null,\"cards\":null,\"labels\":null}")
            )),
            json =>
            {
                // Should deserialize without throwing (fields may be null/default)
                var act = () => JsonSerializer.Deserialize<ImportBoardDto>(json, CamelCaseOptions);
                act.Should().NotThrow("missing fields should result in defaults, not exceptions");
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property StarterPackManifestDto_Roundtrip_PreservesData()
    {
        return Prop.ForAll(
            ValidStarterPackDtoArb(),
            dto =>
            {
                var json = JsonSerializer.Serialize(dto, CamelCaseOptions);
                var deserialized = JsonSerializer.Deserialize<StarterPackManifestDto>(json, CamelCaseOptions);

                deserialized.Should().NotBeNull();
                deserialized!.SchemaVersion.Should().Be(dto.SchemaVersion);
                deserialized.PackId.Should().Be(dto.PackId);
                deserialized.DisplayName.Should().Be(dto.DisplayName);
                deserialized.Labels.Count.Should().Be(dto.Labels.Count);
                deserialized.Columns.Count.Should().Be(dto.Columns.Count);
            });
    }

    private static Arbitrary<ImportBoardDto> ValidImportBoardDtoArb()
    {
        var gen = Gen.Choose(0, 5).SelectMany(colCount =>
            Gen.Choose(0, 5).SelectMany(cardCount =>
            Gen.Choose(0, 3).Select(labelCount =>
            {
                var columns = Enumerable.Range(0, colCount)
                    .Select(i => new ImportColumnDto($"Column{i}", i, i > 0 ? i * 3 : (int?)null))
                    .ToList();

                var columnNames = columns.Select(c => c.Name).ToArray();

                var cards = Enumerable.Range(0, cardCount)
                    .Select(i => new ImportCardDto(
                        $"Card{i}",
                        $"Description for card {i}",
                        columnNames.Length > 0 ? columnNames[i % columnNames.Length] : "Default",
                        i,
                        i % 2 == 0 ? DateTimeOffset.UtcNow.AddDays(i) : null,
                        null))
                    .ToList();

                var labels = Enumerable.Range(0, labelCount)
                    .Select(i =>
                    {
                        var hex = ((i + 1) * 55 % 256).ToString("X2");
                        return new ImportLabelDto($"Label{i}", $"#{hex}{hex}{hex}");
                    })
                    .ToList();

                return new ImportBoardDto(
                    $"Board-{colCount}-{cardCount}",
                    "Test board description",
                    columns,
                    cards,
                    labels);
            })));
        return Arb.From(gen);
    }

    private static Arbitrary<ImportColumnDto> ValidImportColumnDtoArb()
    {
        var gen = Gen.Choose(0, 100).SelectMany(pos =>
            Gen.OneOf(
                Gen.Constant((int?)null),
                Gen.Choose(1, 50).Select(v => (int?)v)
            ).Select(wipLimit =>
                new ImportColumnDto($"Column-{pos}", pos, wipLimit)));
        return Arb.From(gen);
    }

    private static Arbitrary<ImportCardDto> ValidImportCardDtoArb()
    {
        var gen = Gen.Choose(0, 100).Select(pos =>
            new ImportCardDto(
                $"Card-{pos}",
                $"Description for card {pos}",
                "Default",
                pos,
                pos % 2 == 0 ? DateTimeOffset.UtcNow.AddDays(pos) : null,
                pos % 3 == 0 ? new[] { "Label1" } : null));
        return Arb.From(gen);
    }

    private static Arbitrary<ImportLabelDto> ValidImportLabelDtoArb()
    {
        var gen = Gen.Choose(0, 255).Select(i =>
        {
            var hex = i.ToString("X2");
            return new ImportLabelDto($"Label-{i}", $"#{hex}{hex}{hex}");
        });
        return Arb.From(gen);
    }

    private static Arbitrary<StarterPackManifestDto> ValidStarterPackDtoArb()
    {
        var gen = Gen.Choose(1, 3).SelectMany(labelCount =>
            Gen.Choose(1, 4).Select(colCount =>
            {
                var labels = Enumerable.Range(0, labelCount)
                    .Select(i =>
                    {
                        var hex = ((i + 1) * 37 % 256).ToString("X2");
                        return new StarterPackLabelDto
                        {
                            Name = $"Label{i}",
                            Color = $"#{hex}{hex}{hex}"
                        };
                    })
                    .ToList();

                var columns = Enumerable.Range(0, colCount)
                    .Select(i => new StarterPackColumnDto
                    {
                        Name = $"Column{i}",
                        Position = i,
                        WipLimit = i > 0 ? i * 5 : null
                    })
                    .ToList();

                return new StarterPackManifestDto
                {
                    SchemaVersion = "1.0",
                    PackId = "test-pack",
                    DisplayName = "Test Pack",
                    Description = "Fuzz test pack",
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
            }));
        return Arb.From(gen);
    }
}
