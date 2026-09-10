using System.Text.Json;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

public sealed class SpeechTranscriptionSettings
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini-transcribe";
    public bool AllowLocalhostInDevelopment { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
    public int DailyAttempts { get; set; } = 5;
    public long DailyInputBytes { get; set; } = 10 * 1024 * 1024;

    public SpeechTranscriptionConfiguration Validate(string environment)
    {
        if (!Enabled) return new(false, "", "", "", "", 0, 0, 0);
        var allowLocalhost = environment == "Development" && AllowLocalhostInDevelopment;
        var url = SsrfProtectionService.ValidateLlmProviderUrl(BaseUrl, allowLocalhost);
        if (!url.IsAllowed || url.ParsedUri is null || url.ParsedUri.Query.Length > 0 || url.ParsedUri.Fragment.Length > 0
            || string.IsNullOrWhiteSpace(ApiKey) || ApiKey.Length > 4096 || ApiKey.Any(c => c <= ' ' || c > '~')
            || string.IsNullOrWhiteSpace(Model) || Model.Length > 100 || Model.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            || TimeoutSeconds is < 1 or > 90 || DailyAttempts is < 1 or > 20 || DailyInputBytes is < 1 or > 41943040)
            throw new InvalidOperationException("SpeechTranscription requires a safe HTTPS endpoint, credential, model and bounded daily limits. Localhost is allowed only by an explicit Development setting.");
        var endpoint = new Uri(url.ParsedUri.AbsoluteUri.TrimEnd('/') + "/audio/transcriptions");
        var fingerprint = Representation.ComputeTextContentHash(JsonSerializer.Serialize(new
        {
            Schema = 1, Endpoint = endpoint.AbsoluteUri, Provider = "openai-compatible-speech", Model,
            TimeoutSeconds, DailyAttempts, DailyInputBytes
        }));
        return new(true, "openai-compatible-speech", Model, endpoint.GetLeftPart(UriPartial.Authority),
            fingerprint, TimeoutSeconds, DailyAttempts, DailyInputBytes);
    }
}

/// <summary>Public consent and admission policy. Never contains a credential or original content.</summary>
public sealed record SpeechTranscriptionConfiguration(bool Enabled, string Provider, string Model, string Origin,
    string ConfigurationHash, int TimeoutSeconds, int DailyAttempts, long DailyInputBytes);
