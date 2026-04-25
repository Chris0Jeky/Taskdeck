using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

/// <summary>
/// Smoke tests validating that proposal batch JSON payloads survive
/// serialization round-trips. These test the schema contract (matching
/// proposal-batch.v1.schema.json) rather than domain entities directly.
/// </summary>
public class ProposalBatchSchemaRoundTripTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly Lazy<JsonDocument> Schema = new(() =>
        JsonDocument.Parse(File.ReadAllText(FindSchemaPath())));

    private record ProposalBatchDto(
        int SchemaVersion,
        string EnvelopeId,
        string Summary,
        List<ProposalDto> Proposals);

    private record ProposalDto(
        string Summary,
        string RiskLevel,
        string? SourceIntentLabel,
        List<OperationDto> Operations);

    private record OperationDto(
        string OperationType,
        string? TargetId,
        JsonElement Payload);

    private static ProposalBatchDto CreateMinimalBatch()
    {
        var payload = JsonSerializer.SerializeToElement(new { title = "Test card" }, JsonOptions);
        return new ProposalBatchDto(
            SchemaVersion: 1,
            EnvelopeId: Guid.NewGuid().ToString(),
            Summary: "Test batch",
            Proposals: new List<ProposalDto>
            {
                new("Create test card", "Low", null, new List<OperationDto>
                {
                    new("CreateCard", null, payload)
                })
            });
    }

    private static string SerializeAndValidate(ProposalBatchDto batch)
    {
        var json = JsonSerializer.Serialize(batch, JsonOptions);
        ValidateAgainstProposalBatchSchema(json);
        return json;
    }

    private static void ValidateAgainstProposalBatchSchema(string json)
    {
        using var payload = JsonDocument.Parse(json);
        var errors = new List<string>();
        ValidateElement(payload.RootElement, Schema.Value.RootElement, Schema.Value.RootElement, "$", errors);
        errors.Should().BeEmpty("payload should satisfy proposal-batch.v1.schema.json");
    }

    private static string FindSchemaPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "backend",
                "src",
                "Taskdeck.Application",
                "Schemas",
                "proposal-batch.v1.schema.json");
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate proposal-batch.v1.schema.json.");
    }

    private static void ValidateElement(
        JsonElement value,
        JsonElement schema,
        JsonElement rootSchema,
        string path,
        List<string> errors)
    {
        if (schema.TryGetProperty("$ref", out var reference))
            schema = ResolveReference(rootSchema, reference.GetString()!);

        if (schema.TryGetProperty("not", out var notSchema))
        {
            var notErrors = new List<string>();
            ValidateElement(value, notSchema, rootSchema, path, notErrors);
            if (notErrors.Count == 0)
            {
                errors.Add($"{path}: value matched disallowed schema");
                return;
            }
        }

        if (schema.TryGetProperty("allOf", out var allOf))
        {
            foreach (var nestedSchema in allOf.EnumerateArray())
                ValidateElement(value, nestedSchema, rootSchema, path, errors);
        }

        if (schema.TryGetProperty("if", out var ifSchema) &&
            schema.TryGetProperty("then", out var thenSchema))
        {
            var ifErrors = new List<string>();
            ValidateElement(value, ifSchema, rootSchema, path, ifErrors);
            if (ifErrors.Count == 0)
                ValidateElement(value, thenSchema, rootSchema, path, errors);
        }

        if (schema.TryGetProperty("type", out var type) && !MatchesType(value, type))
        {
            errors.Add($"{path}: expected type {type.GetRawText()}, found {value.ValueKind}");
            return;
        }

        if (schema.TryGetProperty("const", out var constant) && !JsonElementEquals(value, constant))
            errors.Add($"{path}: value does not match const {constant.GetRawText()}");

        if (schema.TryGetProperty("enum", out var enumValues) &&
            !enumValues.EnumerateArray().Any(candidate => JsonElementEquals(value, candidate)))
            errors.Add($"{path}: value is not in enum {enumValues.GetRawText()}");

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                ValidateObject(value, schema, rootSchema, path, errors);
                break;
            case JsonValueKind.Array:
                ValidateArray(value, schema, rootSchema, path, errors);
                break;
            case JsonValueKind.String:
                ValidateString(value, schema, path, errors);
                break;
        }
    }

    private static void ValidateObject(
        JsonElement value,
        JsonElement schema,
        JsonElement rootSchema,
        string path,
        List<string> errors)
    {
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var property in required.EnumerateArray().Select(p => p.GetString()!))
            {
                if (!value.TryGetProperty(property, out _))
                    errors.Add($"{path}: missing required property {property}");
            }
        }

        var properties = schema.TryGetProperty("properties", out var declaredProperties)
            ? declaredProperties
            : default;
        var hasProperties = properties.ValueKind == JsonValueKind.Object;
        var allowAdditional = !schema.TryGetProperty("additionalProperties", out var additionalProperties)
            || additionalProperties.ValueKind != JsonValueKind.False;

        foreach (var property in value.EnumerateObject())
        {
            if (hasProperties && properties.TryGetProperty(property.Name, out var propertySchema))
            {
                ValidateElement(property.Value, propertySchema, rootSchema, $"{path}.{property.Name}", errors);
            }
            else if (!allowAdditional)
            {
                errors.Add($"{path}: unexpected property {property.Name}");
            }
        }
    }

    private static void ValidateArray(
        JsonElement value,
        JsonElement schema,
        JsonElement rootSchema,
        string path,
        List<string> errors)
    {
        var count = value.GetArrayLength();
        if (schema.TryGetProperty("minItems", out var minItems) && count < minItems.GetInt32())
            errors.Add($"{path}: expected at least {minItems.GetInt32()} items");
        if (schema.TryGetProperty("maxItems", out var maxItems) && count > maxItems.GetInt32())
            errors.Add($"{path}: expected at most {maxItems.GetInt32()} items");

        if (!schema.TryGetProperty("items", out var itemSchema))
            return;

        var index = 0;
        foreach (var item in value.EnumerateArray())
            ValidateElement(item, itemSchema, rootSchema, $"{path}[{index++}]", errors);
    }

    private static void ValidateString(JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        var text = value.GetString() ?? string.Empty;
        if (schema.TryGetProperty("minLength", out var minLength) && text.Length < minLength.GetInt32())
            errors.Add($"{path}: string is shorter than {minLength.GetInt32()}");
        if (schema.TryGetProperty("maxLength", out var maxLength) && text.Length > maxLength.GetInt32())
            errors.Add($"{path}: string is longer than {maxLength.GetInt32()}");
        if (schema.TryGetProperty("format", out var format) &&
            format.GetString() == "uuid" &&
            !Guid.TryParse(text, out _))
            errors.Add($"{path}: string is not a uuid");
    }

    private static JsonElement ResolveReference(JsonElement rootSchema, string reference)
    {
        var current = rootSchema;
        foreach (var segment in reference.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries))
            current = current.GetProperty(segment);

        return current;
    }

    private static bool MatchesType(JsonElement value, JsonElement type)
    {
        if (type.ValueKind == JsonValueKind.Array)
            return type.EnumerateArray().Any(candidate => MatchesType(value, candidate));

        return type.GetString() switch
        {
            "array" => value.ValueKind == JsonValueKind.Array,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "null" => value.ValueKind == JsonValueKind.Null,
            "object" => value.ValueKind == JsonValueKind.Object,
            "string" => value.ValueKind == JsonValueKind.String,
            _ => true
        };
    }

    private static bool JsonElementEquals(JsonElement left, JsonElement right) =>
        left.ValueKind == right.ValueKind && left.GetRawText() == right.GetRawText();

    [Fact]
    public void Case01_MinimalBatch_ShouldRoundTrip()
    {
        var batch = CreateMinimalBatch();

        var json = SerializeAndValidate(batch);
        var deserialized = JsonSerializer.Deserialize<ProposalBatchDto>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.SchemaVersion.Should().Be(1);
        deserialized.Summary.Should().Be("Test batch");
        deserialized.Proposals.Should().HaveCount(1);
    }

    [Fact]
    public void Case02_MultipleProposals_ShouldRoundTrip()
    {
        var payload = JsonSerializer.SerializeToElement(new { title = "Card" }, JsonOptions);
        var batch = new ProposalBatchDto(
            SchemaVersion: 1,
            EnvelopeId: Guid.NewGuid().ToString(),
            Summary: "Multi-proposal batch",
            Proposals: new List<ProposalDto>
            {
                new("Create card A", "Low", "Intent A", new List<OperationDto>
                {
                    new("CreateCard", null, payload)
                }),
                new("Update card B", "Medium", "Intent B", new List<OperationDto>
                {
                    new("UpdateCard", Guid.NewGuid().ToString(), payload)
                })
            });

        var json = SerializeAndValidate(batch);
        var deserialized = JsonSerializer.Deserialize<ProposalBatchDto>(json, JsonOptions);

        deserialized!.Proposals.Should().HaveCount(2);
        deserialized.Proposals[0].RiskLevel.Should().Be("Low");
        deserialized.Proposals[1].RiskLevel.Should().Be("Medium");
    }

    [Fact]
    public void Case03_AllRiskLevels_ShouldRoundTrip()
    {
        var riskLevels = new[] { "Low", "Medium", "High", "Critical" };
        var payload = JsonSerializer.SerializeToElement(new { title = "X" }, JsonOptions);

        foreach (var risk in riskLevels)
        {
            var batch = new ProposalBatchDto(1, Guid.NewGuid().ToString(), $"Risk: {risk}",
                new List<ProposalDto>
                {
                    new("Op", risk, null, new List<OperationDto>
                    {
                        new("CreateCard", null, payload)
                    })
                });

            var json = SerializeAndValidate(batch);
            var deserialized = JsonSerializer.Deserialize<ProposalBatchDto>(json, JsonOptions);

            deserialized!.Proposals[0].RiskLevel.Should().Be(risk);
        }
    }

    [Fact]
    public void Case04_AllOperationTypes_ShouldRoundTrip()
    {
        var opTypes = new[]
        {
            "CreateCard", "UpdateCard", "MoveCard", "DeleteCard",
            "CreateColumn", "UpdateColumn", "CreateLabel", "AddLabel", "RemoveLabel"
        };
        var payload = JsonSerializer.SerializeToElement(new { data = "test" }, JsonOptions);

        var operations = opTypes.Select(op => new OperationDto(op, Guid.NewGuid().ToString(), payload)).ToList();
        var batch = new ProposalBatchDto(1, Guid.NewGuid().ToString(), "All ops",
            new List<ProposalDto> { new("All operations", "Low", null, operations) });

        var json = SerializeAndValidate(batch);
        var deserialized = JsonSerializer.Deserialize<ProposalBatchDto>(json, JsonOptions);

        deserialized!.Proposals[0].Operations.Should().HaveCount(opTypes.Length);
        for (var i = 0; i < opTypes.Length; i++)
            deserialized.Proposals[0].Operations[i].OperationType.Should().Be(opTypes[i]);
    }

    [Fact]
    public void Case05_NullOptionalFields_ShouldRoundTrip()
    {
        var payload = JsonSerializer.SerializeToElement(new { title = "X" }, JsonOptions);
        var batch = new ProposalBatchDto(1, Guid.NewGuid().ToString(), "Nulls test",
            new List<ProposalDto>
            {
                new("Op", "Low", null, new List<OperationDto>
                {
                    new("CreateCard", null, payload)
                })
            });

        var json = SerializeAndValidate(batch);
        var deserialized = JsonSerializer.Deserialize<ProposalBatchDto>(json, JsonOptions);

        deserialized!.Proposals[0].SourceIntentLabel.Should().BeNull();
        deserialized.Proposals[0].Operations[0].TargetId.Should().BeNull();
    }

    [Fact]
    public void Case06_ComplexPayload_ShouldPreserveStructure()
    {
        var complexPayload = JsonSerializer.SerializeToElement(new
        {
            title = "Complex card",
            description = "With nested data",
            labels = new[] { "bug", "priority" },
            metadata = new { priority = 1, estimate = 3.5 }
        }, JsonOptions);

        var batch = new ProposalBatchDto(1, Guid.NewGuid().ToString(), "Complex payload",
            new List<ProposalDto>
            {
                new("Create complex", "Medium", "Complex intent", new List<OperationDto>
                {
                    new("CreateCard", null, complexPayload)
                })
            });

        var json = SerializeAndValidate(batch);
        var deserialized = JsonSerializer.Deserialize<ProposalBatchDto>(json, JsonOptions);

        var payloadBack = deserialized!.Proposals[0].Operations[0].Payload;
        payloadBack.GetProperty("title").GetString().Should().Be("Complex card");
        payloadBack.GetProperty("labels").GetArrayLength().Should().Be(2);
        payloadBack.GetProperty("metadata").GetProperty("priority").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Case07_UuidFormatPreserved()
    {
        var envelopeId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var payload = JsonSerializer.SerializeToElement(new { x = 1 }, JsonOptions);

        var batch = new ProposalBatchDto(1, envelopeId.ToString(), "UUID test",
            new List<ProposalDto>
            {
                new("Op", "Low", null, new List<OperationDto>
                {
                    new("UpdateCard", targetId.ToString(), payload)
                })
            });

        var json = SerializeAndValidate(batch);
        var deserialized = JsonSerializer.Deserialize<ProposalBatchDto>(json, JsonOptions);

        Guid.Parse(deserialized!.EnvelopeId).Should().Be(envelopeId);
        Guid.Parse(deserialized.Proposals[0].Operations[0].TargetId!).Should().Be(targetId);
    }

    [Fact]
    public void Case08_LongSummary_ShouldRoundTrip()
    {
        var longSummary = new string('A', 1000);
        var payload = JsonSerializer.SerializeToElement(new { x = 1 }, JsonOptions);

        var batch = new ProposalBatchDto(1, Guid.NewGuid().ToString(), longSummary,
            new List<ProposalDto>
            {
                new(new string('B', 500), "High", new string('C', 500), new List<OperationDto>
                {
                    new("CreateCard", null, payload)
                })
            });

        var json = SerializeAndValidate(batch);
        var deserialized = JsonSerializer.Deserialize<ProposalBatchDto>(json, JsonOptions);

        deserialized!.Summary.Length.Should().Be(1000);
        deserialized.Proposals[0].Summary.Length.Should().Be(500);
        deserialized.Proposals[0].SourceIntentLabel!.Length.Should().Be(500);
    }

    [Fact]
    public void Case09_MultipleOperationsPerProposal_ShouldRoundTrip()
    {
        var payload1 = JsonSerializer.SerializeToElement(new { title = "Card A" }, JsonOptions);
        var payload2 = JsonSerializer.SerializeToElement(new { labelName = "urgent" }, JsonOptions);
        var payload3 = JsonSerializer.SerializeToElement(new { columnId = Guid.NewGuid() }, JsonOptions);

        var batch = new ProposalBatchDto(1, Guid.NewGuid().ToString(), "Multi-op",
            new List<ProposalDto>
            {
                new("Multiple ops", "Medium", null, new List<OperationDto>
                {
                    new("CreateCard", null, payload1),
                    new("AddLabel", Guid.NewGuid().ToString(), payload2),
                    new("MoveCard", Guid.NewGuid().ToString(), payload3)
                })
            });

        var json = SerializeAndValidate(batch);
        var deserialized = JsonSerializer.Deserialize<ProposalBatchDto>(json, JsonOptions);

        deserialized!.Proposals[0].Operations.Should().HaveCount(3);
        deserialized.Proposals[0].Operations[0].OperationType.Should().Be("CreateCard");
        deserialized.Proposals[0].Operations[1].OperationType.Should().Be("AddLabel");
        deserialized.Proposals[0].Operations[2].OperationType.Should().Be("MoveCard");
    }

    [Fact]
    public void Case10_EmptyPayload_ShouldRoundTrip()
    {
        var emptyPayload = JsonSerializer.SerializeToElement(new { }, JsonOptions);

        var batch = new ProposalBatchDto(1, Guid.NewGuid().ToString(), "Empty payload test",
            new List<ProposalDto>
            {
                new("Delete op", "Critical", null, new List<OperationDto>
                {
                    new("DeleteCard", Guid.NewGuid().ToString(), emptyPayload)
                })
            });

        var json = SerializeAndValidate(batch);
        var deserialized = JsonSerializer.Deserialize<ProposalBatchDto>(json, JsonOptions);

        deserialized!.Proposals[0].Operations[0].Payload.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void InvalidRiskLevel_ShouldFailSchemaValidation()
    {
        var payload = JsonSerializer.SerializeToElement(new { title = "X" }, JsonOptions);
        var batch = new ProposalBatchDto(1, Guid.NewGuid().ToString(), "Invalid risk",
            new List<ProposalDto>
            {
                new("Op", "Severe", null, new List<OperationDto>
                {
                    new("CreateCard", null, payload)
                })
            });
        var json = JsonSerializer.Serialize(batch, JsonOptions);

        var act = () => ValidateAgainstProposalBatchSchema(json);

        act.Should().Throw<Xunit.Sdk.XunitException>();
    }

    [Theory]
    [InlineData("UpdateCard")]
    [InlineData("MoveCard")]
    [InlineData("DeleteCard")]
    [InlineData("UpdateColumn")]
    [InlineData("AddLabel")]
    [InlineData("RemoveLabel")]
    public void NonCreateOperation_ShouldFailSchemaValidation_WhenTargetIdIsMissingOrNull(string operationType)
    {
        var payload = JsonSerializer.SerializeToElement(new { title = "X" }, JsonOptions);
        var batch = new ProposalBatchDto(1, Guid.NewGuid().ToString(), "Missing target",
            new List<ProposalDto>
            {
                new("Op", "Low", null, new List<OperationDto>
                {
                    new(operationType, null, payload)
                })
            });
        var json = JsonSerializer.Serialize(batch, JsonOptions);

        var act = () => ValidateAgainstProposalBatchSchema(json);

        act.Should().Throw<Xunit.Sdk.XunitException>();
    }
}
