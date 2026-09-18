using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Api.Tests;

public class SpeechTranscriptionTransportTests
{
    [Fact]
    public async Task RegisteredSpeechTransport_SendsMultipartToExplicitLocalFixture_AndDisclosesWithoutSecrets()
    {
        var calls = 0; byte[]? received = null; string? auth = null; string? trace = null; string? model = null;
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        await using var server = builder.Build();
        server.MapPost("/v1/audio/transcriptions", async context =>
        {
            Interlocked.Increment(ref calls); auth = context.Request.Headers.Authorization; trace = context.Request.Headers.TraceParent;
            var form = await context.Request.ReadFormAsync(); model = form["model"];
            using var output = new MemoryStream(); await form.Files.Single().CopyToAsync(output); received = output.ToArray();
            context.Response.ContentType = "application/json"; await context.Response.WriteAsync("{\"text\":\"Transport fixture transcript\"}");
        });
        await server.StartAsync();
        try
        {
            var endpoint = new UriBuilder(server.Urls.Single()) { Host = "localhost", Path = "/v1/" }.Uri.AbsoluteUri;
            using var services = Services(endpoint);
            var entries = services.GetRequiredService<IEgressRegistry>().GetAllEntries();
            var disclosure = entries.Single(x => x.ToolOrAgentName == nameof(HttpAudioTranscriptionProvider));
            disclosure.Host.Should().Be("localhost"); disclosure.Classification.Should().Be(EgressDataClassification.UserContent);
            disclosure.ToString().Should().NotContain("synthetic-speech-key");
            using var scope = services.CreateScope(); var provider = scope.ServiceProvider.GetRequiredService<IAudioTranscriptionProvider>();
            using var activity = new Activity("speech-fixture").SetIdFormat(ActivityIdFormat.W3C).Start();
            var result = await provider.TranscribeAsync([1, 2, 3, 4], "audio/ogg", default);
            result.Text.Should().Be("Transport fixture transcript"); result.FailureCode.Should().BeNull();
            received.Should().Equal(1, 2, 3, 4); auth.Should().Be("Bearer synthetic-speech-key"); trace.Should().BeNullOrEmpty();
            model.Should().Be("fixture-model"); calls.Should().Be(1);
        }
        finally { await server.StopAsync(); }
    }

    [Theory]
    [InlineData(302)]
    [InlineData(503)]
    public async Task RedirectsAreNotFollowed_AndProviderFailureIsBoundedBySharedCircuit(int responseStatus)
    {
        var calls = 0; var redirected = 0;
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        await using var server = builder.Build();
        server.MapPost("/v1/audio/transcriptions", context =>
        {
            Interlocked.Increment(ref calls); context.Response.StatusCode = responseStatus;
            context.Response.Headers.Location = "/unexpected"; return Task.CompletedTask;
        });
        server.Map("/unexpected", () => { Interlocked.Increment(ref redirected); return Results.Ok(); });
        await server.StartAsync();
        try
        {
            var endpoint = new UriBuilder(server.Urls.Single()) { Host = "localhost", Path = "/v1/" }.Uri.AbsoluteUri;
            using var services = Services(endpoint); using var scope = services.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<IAudioTranscriptionProvider>();
            (await provider.TranscribeAsync([1], "audio/webm", default)).FailureCode.Should().Be("provider-unavailable");
            (await provider.TranscribeAsync([1], "audio/webm", default)).FailureCode.Should().Be("provider-unavailable");
            redirected.Should().Be(0); calls.Should().Be(responseStatus == 503 ? 1 : 2);
        }
        finally { await server.StopAsync(); }
    }

    private static ServiceProvider Services(string endpoint)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SpeechTranscription:Enabled"] = "true", ["SpeechTranscription:BaseUrl"] = endpoint,
            ["SpeechTranscription:ApiKey"] = "synthetic-speech-key", ["SpeechTranscription:Model"] = "fixture-model",
            ["SpeechTranscription:AllowLocalhostInDevelopment"] = "true", ["CircuitBreaker:FailureThreshold"] = "1"
        }).Build();
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IConfiguration>(config);
        services.AddLlmProviders(config); services.AddSpeechTranscription(config, "Development");
        return services.BuildServiceProvider();
    }
}
