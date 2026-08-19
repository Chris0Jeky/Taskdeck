using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Auth matrix and payload contract for the transcript read surface that backs the
/// Review "view in transcript" affordance.
/// </summary>
public sealed class TranscriptsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public TranscriptsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetById_ShouldRequireAuthentication()
    {
        using var client = _factory.CreateClient();

        await ApiTestHarness.AssertUnauthorizedAsync(
            await client.GetAsync($"/api/transcripts/{Guid.NewGuid()}"));
    }

    [Fact]
    public async Task GetById_ShouldReturnOwnTranscriptTextAndSegments()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "transcript-read-owner");

        const string text = "Ada: ship the export fix\nGrace: I will take the migration";
        var transcriptId = await SeedTranscriptAsync(
            user.UserId,
            text,
            [
                new TranscriptSegment(0, 0, "Ada", 0),
                new TranscriptSegment(1, 1, "Grace", 4200),
            ]);

        var response = await client.GetAsync($"/api/transcripts/{transcriptId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var transcript = await response.Content.ReadFromJsonAsync<TranscriptDto>();
        transcript.Should().NotBeNull();
        transcript!.Id.Should().Be(transcriptId);
        transcript.Text.Should().Be(text);
        transcript.CaptureSource.Should().Be(CaptureSource.TranscriptPaste);
        transcript.Segments.Should().HaveCount(2);
        transcript.Segments[0].Should().BeEquivalentTo(new TranscriptSegmentDto(0, 0, "Ada", 0));
        transcript.Segments[1].Should().BeEquivalentTo(new TranscriptSegmentDto(1, 1, "Grace", 4200));
    }

    [Fact]
    public async Task GetById_ShouldReturnNotFoundForAnUnknownTranscript()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "transcript-read-missing");

        var response = await client.GetAsync($"/api/transcripts/{Guid.NewGuid()}");

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, "NotFound");
    }

    [Fact]
    public async Task GetById_ShouldRejectTheEmptyGuidAsAValidationError()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "transcript-read-empty-id");

        // The all-zero GUID satisfies the route constraint but can never identify a
        // transcript; it is rejected as malformed input rather than searched for.
        var response = await client.GetAsync($"/api/transcripts/{Guid.Empty}");

        await ApiTestHarness.AssertErrorContractAsync(
            response,
            HttpStatusCode.BadRequest,
            "ValidationError");
    }

    [Fact]
    public async Task GetById_ShouldNotRevealAnotherUsersTranscript()
    {
        using var ownerClient = _factory.CreateClient();
        using var outsiderClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "transcript-read-owner2");
        await ApiTestHarness.AuthenticateAsync(outsiderClient, "transcript-read-outsider");

        const string privateText = "PRIVATE_TRANSCRIPT_TEXT_never_return_this";
        var transcriptId = await SeedTranscriptAsync(owner.UserId, privateText, []);

        var foreignResponse = await outsiderClient.GetAsync($"/api/transcripts/{transcriptId}");
        var missingResponse = await outsiderClient.GetAsync($"/api/transcripts/{Guid.NewGuid()}");

        // Byte-identical outcomes: an existing-but-foreign transcript and a nonexistent one
        // must be indistinguishable, or the endpoint becomes a cross-user existence oracle.
        foreignResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var foreignBody = await foreignResponse.Content.ReadAsStringAsync();
        var missingBody = await missingResponse.Content.ReadAsStringAsync();
        foreignBody.Should().Be(missingBody);
        foreignBody.Should().NotContain(privateText);
    }

    [Fact]
    public async Task GetById_ShouldPreserveCharacterOffsetsAcrossMultiByteTextAndCrlf()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "transcript-read-multibyte");

        // Astral-plane emoji (surrogate pairs), a combining-accent name, and CRLF line
        // endings — everything that could shift a char offset between store and wire.
        const string quote = "déployer le correctif 🚀 aujourd'hui";
        const string source = "Zoë 🧭 : contexte\r\n" + quote + "\r\nfin 🎉";
        var expectedText = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var expectedStart = expectedText.IndexOf(quote, StringComparison.Ordinal);
        expectedStart.Should().BeGreaterThan(0);
        var expectedEnd = expectedStart + quote.Length;

        var transcriptId = await SeedTranscriptAsync(user.UserId, source, []);

        var response = await client.GetAsync($"/api/transcripts/{transcriptId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var transcript = await response.Content.ReadFromJsonAsync<TranscriptDto>();

        transcript.Should().NotBeNull();
        // The persisted text is LF-normalized, and evidence spans are offsets into that
        // normalized form; the wire representation must not renormalize or re-encode it.
        transcript!.Text.Should().Be(expectedText);
        transcript.Text.Should().NotContain("\r");
        transcript.Text[expectedStart..expectedEnd].Should().Be(quote);
    }

    private async Task<Guid> SeedTranscriptAsync(
        Guid userId,
        string text,
        IReadOnlyList<TranscriptSegment> segments)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var transcript = new Transcript(userId, CaptureSource.TranscriptPaste, text, segments);
        db.Transcripts.Add(transcript);
        await db.SaveChangesAsync();
        return transcript.Id;
    }
}
