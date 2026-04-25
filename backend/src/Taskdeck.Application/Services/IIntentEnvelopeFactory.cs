using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

/// <summary>
/// Factory for creating <see cref="IntentEnvelopeV1"/> instances from
/// different input surfaces (Capture, Chat). Implementations live in
/// Infrastructure or Application depending on whether external dependencies
/// are required.
///
/// This is a PARALLEL path alongside the existing capture/chat pipelines.
/// It does not replace or modify existing behavior -- it produces envelopes
/// that can be consumed by future pipeline stages.
/// </summary>
public interface IIntentEnvelopeFactory
{
    /// <summary>
    /// Creates an envelope from a capture payload. The returned envelope
    /// is in <see cref="EnvelopeStatus.Created"/> status with source blocks
    /// pre-populated from the payload content.
    /// </summary>
    /// <param name="userId">The user who triggered the capture.</param>
    /// <param name="rawContent">Raw text content from the capture input.</param>
    /// <param name="captureItemId">Optional ID of the originating capture item for correlation.</param>
    /// <param name="capturedAt">When the input was captured. Defaults to now.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Result containing the created envelope, or an error.</returns>
    Task<Result<IntentEnvelopeV1>> CreateFromCaptureAsync(
        Guid userId,
        string rawContent,
        Guid? captureItemId = null,
        DateTimeOffset? capturedAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an envelope from a chat message. The returned envelope
    /// is in <see cref="EnvelopeStatus.Created"/> status with source blocks
    /// pre-populated from the message content.
    /// </summary>
    /// <param name="userId">The user who sent the message.</param>
    /// <param name="rawContent">Raw text content from the chat message.</param>
    /// <param name="sessionId">The chat session ID for correlation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Result containing the created envelope, or an error.</returns>
    Task<Result<IntentEnvelopeV1>> CreateFromChatAsync(
        Guid userId,
        string rawContent,
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
