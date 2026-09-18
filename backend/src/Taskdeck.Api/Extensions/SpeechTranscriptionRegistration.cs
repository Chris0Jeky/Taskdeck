using Taskdeck.Api.Workers;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Extensions;

public static class SpeechTranscriptionRegistration
{
    public static IServiceCollection AddSpeechTranscription(this IServiceCollection services, IConfiguration configuration, string environment)
    {
        var settings = configuration.GetSection("SpeechTranscription").Get<SpeechTranscriptionSettings>() ?? new();
        var policy = settings.Validate(environment);
        // AddLlmProviders owns these application-wide instances; reuse the same disclosure registry and tracker.
        var registry = services.Last(x => x.ServiceType == typeof(IEgressRegistry)).ImplementationInstance as IEgressRegistry
            ?? throw new InvalidOperationException("Register the shared provider egress registry before speech transcription.");
        if (policy.Enabled) registry.Register(new(new Uri(policy.Origin).Host,
            "Explicitly selected original recording for provisional transcription", nameof(HttpAudioTranscriptionProvider), EgressDataClassification.UserContent));
        var tracker = (CircuitBreakerStateTracker)services.Last(x => x.ServiceType == typeof(CircuitBreakerStateTracker)).ImplementationInstance!;
        var circuitSettings = (CircuitBreakerSettings)services.Last(x => x.ServiceType == typeof(CircuitBreakerSettings)).ImplementationInstance!;
        var circuit = LlmProviderRegistration.BuildCircuitBreakerPolicy(tracker, "SpeechTranscription", circuitSettings);
        services.AddSingleton(settings);
        services.AddSingleton(policy);
        services.AddScoped<AudioTranscriptionService>();
        services.AddHttpClient<HttpAudioTranscriptionProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseProxy = false, ActivityHeadersPropagator = null,
                MeterFactory = sp.GetRequiredService<ProtectedOutboundMeterFactory>(),
                ConnectCallback = (context, ct) => OutboundWebhookConnectCallback.ConnectAsync(context,
                    environment == "Development" && settings.AllowLocalhostInDevelopment, ct)
            })
            .RemoveAllLoggers()
            .AddHttpMessageHandler(() => new SpeechCircuitFailureHandler())
            .AddPolicyHandler(circuit)
            .AddHttpMessageHandler<ProtectedOutboundTelemetryHandler>()
            .AddHttpMessageHandler(sp => new EgressEnvelopeHandler(sp.GetRequiredService<IEgressRegistry>(),
                sp.GetRequiredService<ILogger<EgressEnvelopeHandler>>(), nameof(HttpAudioTranscriptionProvider), followRedirects: false));
        services.AddScoped<IAudioTranscriptionProvider>(sp => sp.GetRequiredService<HttpAudioTranscriptionProvider>());
        return services;
    }

    private sealed class SpeechCircuitFailureHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try { return await base.SendAsync(request, cancellationToken); }
            catch (Polly.CircuitBreaker.BrokenCircuitException)
            { return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable); }
        }
    }
}
