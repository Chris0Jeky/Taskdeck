using Microsoft.Extensions.Http;
using Sentry;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Extensions;

/// <summary>
/// Removes Sentry's factory-wide HTTP handler only from Taskdeck's registered
/// protected outbound clients. Other named and typed clients retain Sentry's
/// normal outgoing-request instrumentation.
/// </summary>
internal sealed class ProtectedOutboundSentryHttpMessageHandlerFilter : IHttpMessageHandlerBuilderFilter
{
    private static readonly HashSet<string> ProtectedClientNames = new(StringComparer.Ordinal)
    {
        nameof(OpenAiLlmProvider),
        nameof(GeminiLlmProvider),
        nameof(OllamaLlmProvider),
        "OutboundWebhookDelivery"
    };

    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return builder =>
        {
            next(builder);

            var clientName = builder.Name;
            if (clientName is null || !ProtectedClientNames.Contains(clientName))
            {
                return;
            }

            for (var index = builder.AdditionalHandlers.Count - 1; index >= 0; index--)
            {
                if (builder.AdditionalHandlers[index] is SentryHttpMessageHandler)
                {
                    builder.AdditionalHandlers.RemoveAt(index);
                }
            }
        };
    }
}
