using System.Text.Json;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Fuzz;

/// <summary>
/// Property-based tests for capture payload and provenance serialization round-trips.
/// Key properties:
/// - CapturePayloadV1 survives JSON serialize/deserialize with identity
/// - CaptureProvenanceV1 fields are always recoverable after round-trip
/// - CaptureItemDto provenance is preserved through serialization
/// </summary>
public class CaptureProvenanceRoundTripFuzzTests
{
    private const int MaxTests = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    // ─────────────────────── Adversarial string generators ───────────────────────

    private static Gen<string> AdversarialStringGen() => Gen.OneOf(
        Gen.Constant("\u0000"),
        Gen.Constant("\uFEFF"),
        Gen.Constant("\u200B"),
        Gen.Constant("<script>alert('xss')</script>"),
        Gen.Constant("'; DROP TABLE capture_items; --"),
        Gen.Constant("{\"nested\": true}"),
        Gen.Constant("emoji 👨‍👩‍👧‍👦"),
        Gen.Constant("田中太郎"),
        Gen.Constant("back\\slash"),
        Gen.Constant("new\nline\ttab"),
        Gen.Constant(""),
        ArbMap.Default.ArbFor<string>().Generator.Where(s => s != null)
    );

    private static Gen<string?> NullableStringGen() => Gen.OneOf(
        Gen.Constant((string?)null),
        AdversarialStringGen().Select(s => (string?)s)
    );

    // ─────────────────────── CaptureProvenanceV1 round-trip ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property CaptureProvenanceV1_RoundTrip_AllFieldsPreserved()
    {
        return Prop.ForAll(
            CaptureProvenanceArb(),
            provenance =>
            {
                var json = JsonSerializer.Serialize(provenance, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<CaptureProvenanceV1>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.CaptureItemId.Should().Be(provenance.CaptureItemId);
                deserialized.TriageRunId.Should().Be(provenance.TriageRunId);
                deserialized.ProposalId.Should().Be(provenance.ProposalId);
                deserialized.PromptVersion.Should().Be(provenance.PromptVersion);
                deserialized.Provider.Should().Be(provenance.Provider);
                deserialized.Model.Should().Be(provenance.Model);
                deserialized.RequestedByUserId.Should().Be(provenance.RequestedByUserId);
                deserialized.CorrelationId.Should().Be(provenance.CorrelationId);
                deserialized.SourceSurface.Should().Be(provenance.SourceSurface);
                deserialized.BoardId.Should().Be(provenance.BoardId);
                deserialized.SessionId.Should().Be(provenance.SessionId);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property CaptureProvenanceV1_WithAdversarialStrings_RoundTrips()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            Arb.From(AdversarialStringGen()),
            Arb.From(AdversarialStringGen()),
            (promptVersion, provider, model) =>
            {
                var provenance = new CaptureProvenanceV1(
                    Guid.NewGuid(),
                    PromptVersion: promptVersion,
                    Provider: provider,
                    Model: model);

                var json = JsonSerializer.Serialize(provenance, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<CaptureProvenanceV1>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.PromptVersion.Should().Be(promptVersion);
                deserialized.Provider.Should().Be(provider);
                deserialized.Model.Should().Be(model);
            });
    }

    // ─────────────────────── CapturePayloadV1 round-trip ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property CapturePayloadV1_RoundTrip_PreservesAllFields()
    {
        return Prop.ForAll(
            CapturePayloadArb(),
            payload =>
            {
                var json = JsonSerializer.Serialize(payload, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<CapturePayloadV1>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Version.Should().Be(payload.Version);
                deserialized.Source.Should().Be(payload.Source);
                deserialized.Text.Should().Be(payload.Text);
                deserialized.TitleHint.Should().Be(payload.TitleHint);
                deserialized.ExternalRef.Should().Be(payload.ExternalRef);

                if (payload.Provenance is not null)
                {
                    deserialized.Provenance.Should().NotBeNull();
                    deserialized.Provenance!.CaptureItemId.Should().Be(payload.Provenance.CaptureItemId);
                }
                else
                {
                    deserialized.Provenance.Should().BeNull();
                }
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property CapturePayloadV1_WithAdversarialText_RoundTrips()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            text =>
            {
                var payload = new CapturePayloadV1(
                    1,
                    CaptureSource.Typed,
                    text);

                var json = JsonSerializer.Serialize(payload, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<CapturePayloadV1>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Text.Should().Be(text);
            });
    }

    // ─────────────────────── CreateCaptureItemDto round-trip ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property CreateCaptureItemDto_RoundTrip_Identity()
    {
        return Prop.ForAll(
            Arb.From(AdversarialStringGen()),
            Arb.From(NullableStringGen()),
            Arb.From(NullableStringGen()),
            (text, titleHint, externalRef) =>
            {
                var dto = new CreateCaptureItemDto(null, text,
                    TitleHint: titleHint, ExternalRef: externalRef);

                var json = JsonSerializer.Serialize(dto, JsonOptions);
                var deserialized = JsonSerializer.Deserialize<CreateCaptureItemDto>(json, JsonOptions);

                deserialized.Should().NotBeNull();
                deserialized!.Text.Should().Be(text);
                deserialized.TitleHint.Should().Be(titleHint);
                deserialized.ExternalRef.Should().Be(externalRef);
                deserialized.BoardId.Should().BeNull();
            });
    }

    // ─────────────────────── Deserialization safety ───────────────────────

    [Property(MaxTest = MaxTests)]
    public Property CapturePayloadV1_ArbitraryJson_NeverThrowsUnhandled()
    {
        return Prop.ForAll(
            ArbMap.Default.ArbFor<string>(),
            json =>
            {
                try
                {
                    JsonSerializer.Deserialize<CapturePayloadV1>(json ?? "null", JsonOptions);
                }
                catch (JsonException)
                {
                    // Expected for malformed JSON
                }
                catch (Exception ex)
                {
                    ex.Should().BeNull(
                        $"Unexpected exception type {ex.GetType()}: {ex.Message}");
                }
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property CaptureProvenanceV1_ArbitraryJson_NeverThrowsUnhandled()
    {
        return Prop.ForAll(
            ArbMap.Default.ArbFor<string>(),
            json =>
            {
                try
                {
                    JsonSerializer.Deserialize<CaptureProvenanceV1>(json ?? "null", JsonOptions);
                }
                catch (JsonException)
                {
                    // Expected
                }
                catch (Exception ex)
                {
                    ex.Should().BeNull(
                        $"Unexpected exception type {ex.GetType()}: {ex.Message}");
                }
            });
    }

    // ─────────────────────── DateTimeOffset edge cases in provenance ───────────────────────

    [Theory]
    [InlineData("0001-01-01T00:00:00+00:00")]
    [InlineData("9999-12-31T23:59:59.9999999+00:00")]
    [InlineData("2026-04-10T00:00:00+00:00")]
    [InlineData("1970-01-01T00:00:00+00:00")]      // Unix epoch
    public void CaptureProvenance_WithDateTimeBoundaries_RoundTrips(string dateStr)
    {
        var date = DateTimeOffset.Parse(dateStr);
        var provenance = new CaptureProvenanceV1(
            Guid.NewGuid(),
            ConvertedAt: date);

        var json = JsonSerializer.Serialize(provenance, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<CaptureProvenanceV1>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.ConvertedAt.Should().Be(date);
    }

    // ─────────────────────── Arbitrary generators ───────────────────────

    private static Arbitrary<CaptureProvenanceV1> CaptureProvenanceArb()
    {
        var gen = Gen.OneOf(
            Gen.Constant(true),
            Gen.Constant(false)
        ).SelectMany(hasOptionals =>
        {
            if (!hasOptionals)
            {
                return Gen.Constant(new CaptureProvenanceV1(Guid.NewGuid()));
            }

            return Gen.Fresh(() =>
                new CaptureProvenanceV1(
                    Guid.NewGuid(),
                    TriageRunId: Guid.NewGuid(),
                    ProposalId: Guid.NewGuid(),
                    PromptVersion: "v1.0",
                    Provider: "mock",
                    Model: "mock-model",
                    RequestedByUserId: Guid.NewGuid(),
                    CorrelationId: Guid.NewGuid().ToString(),
                    SourceSurface: "inbox",
                    BoardId: Guid.NewGuid(),
                    SessionId: Guid.NewGuid(),
                    ConvertedAt: DateTimeOffset.UtcNow));
        });

        return Arb.From(gen);
    }

    private static Arbitrary<CapturePayloadV1> CapturePayloadArb()
    {
        var gen = Gen.OneOf(
            Gen.Elements(CaptureSource.Typed, CaptureSource.Paste, CaptureSource.Import)
        ).SelectMany(source =>
            AdversarialStringGen().SelectMany(text =>
            Gen.OneOf(
                Gen.Constant(true),
                Gen.Constant(false)
            ).Select(hasProvenance =>
            {
                var provenance = hasProvenance
                    ? new CaptureProvenanceV1(Guid.NewGuid())
                    : null;

                return new CapturePayloadV1(
                    1,
                    source,
                    text,
                    TitleHint: text.Length > 5 ? text[..5] : null,
                    ExternalRef: null,
                    Provenance: provenance);
            })));

        return Arb.From(gen);
    }
}
