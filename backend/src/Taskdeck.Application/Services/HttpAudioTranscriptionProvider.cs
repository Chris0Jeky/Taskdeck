using System.Net.Http.Headers;
using System.Text.Json;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Application.Services;

/// <summary>One bounded multipart request. Retries require a new explicit, durably admitted attempt.</summary>
public sealed class HttpAudioTranscriptionProvider : IAudioTranscriptionProvider
{
    public const int MaximumResponseBytes = 65536;
    public const int MaximumTextLength = 8000;
    private readonly HttpClient client;
    private readonly string apiKey;
    private readonly Uri? endpoint;
    public SpeechTranscriptionConfiguration Configuration { get; }

    public HttpAudioTranscriptionProvider(HttpClient client, SpeechTranscriptionSettings settings, SpeechTranscriptionConfiguration configuration)
    {
        this.client = client; Configuration = configuration; apiKey = settings.ApiKey;
        endpoint = configuration.Enabled ? new Uri(settings.BaseUrl.Trim().TrimEnd('/') + "/audio/transcriptions") : null;
    }

    public async Task<AudioTranscriptionProviderResult> TranscribeAsync(byte[] original, string mediaType, CancellationToken ct)
    {
        if (!Configuration.Enabled) return new(null, "provider-unavailable");
        var extension = mediaType switch
        {
            "audio/webm" => "webm", "audio/ogg" => "ogg", "audio/wav" or "audio/x-wav" => "wav",
            "audio/mpeg" => "mp3", "audio/mp4" => "mp4", _ => null
        };
        if (extension is null || original.LongLength is <= 0 or > ThinkingAudioService.MaximumBytes)
            return new(null, "input-unavailable");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Configuration.TimeoutSeconds));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var multipart = new MultipartFormDataContent();
            using var file = new ByteArrayContent(original);
            file.Headers.ContentType = new MediaTypeHeaderValue(mediaType == "audio/x-wav" ? "audio/wav" : mediaType);
            // A constant filename avoids disclosing the user's title or original filesystem name.
            multipart.Add(file, "file", "recording." + extension);
            multipart.Add(new StringContent(Configuration.Model), "model");
            multipart.Add(new StringContent("json"), "response_format");
            request.Content = multipart;
            ProtectedOutboundTelemetryHandler.PrepareForSend(request);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return new(null, "provider-unavailable");
            if (response.Content.Headers.ContentType?.MediaType != "application/json"
                || response.Content.Headers.ContentLength > MaximumResponseBytes)
                return new(null, "provider-response");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (output.Length + count > MaximumResponseBytes) return new(null, "provider-response");
                output.Write(buffer, 0, count);
            }
            using var json = JsonDocument.Parse(output.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            if (json.RootElement.ValueKind != JsonValueKind.Object) return new(null, "provider-response");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in json.RootElement.EnumerateObject())
                if (!names.Add(property.Name)) return new(null, "provider-response");
            if (!json.RootElement.TryGetProperty("text", out var value) || value.ValueKind != JsonValueKind.String)
                return new(null, "provider-response");
            var text = value.GetString();
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumTextLength
                || text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
                return new(null, "provider-response");
            return new(text.Trim(), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(null, "provider-timeout"); }
        catch (HttpRequestException) { return new(null, "provider-unavailable"); }
        catch (EgressViolationException) { return new(null, "provider-unavailable"); }
        catch (IOException) { return new(null, "provider-unavailable"); }
        catch (JsonException) { return new(null, "provider-response"); }
    }
}
