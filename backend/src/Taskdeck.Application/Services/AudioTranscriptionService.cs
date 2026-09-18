using System.Security.Cryptography;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class AudioTranscriptionService(IAudioTranscriptionStore store, IAudioTranscriptionProvider provider,
    IBlobStore blobs, ILlmKillSwitchService killSwitch, TimeProvider time)
{
    public async Task<Result<AudioTranscriptionStatusDto>> StatusAsync(Guid userId, Guid audioId, CancellationToken ct)
    {
        var configuration = provider.Configuration;
        if (await killSwitch.IsKilledAsync(LlmSurface.Worker, userId, ct)) configuration = configuration with { Enabled = false };
        return await store.StatusAsync(userId, audioId, configuration, time.GetUtcNow(), ct);
    }

    public async Task<Result<AudioTranscriptionReceiptDto>> StartAsync(Guid userId, Guid audioId, AudioTranscriptionRequestDto request, CancellationToken ct)
    {
        if (await killSwitch.IsKilledAsync(LlmSurface.Worker, userId, ct))
            return Result.Failure<AudioTranscriptionReceiptDto>(ErrorCodes.ValidationError, "Automatic processing is paused. Existing recordings and receipts remain available.");
        var admission = await store.AdmitAsync(userId, audioId, request, provider.Configuration, time.GetUtcNow(), ct);
        if (!admission.IsSuccess) return Result.Failure<AudioTranscriptionReceiptDto>(admission.ErrorCode, admission.ErrorMessage);
        if (admission.Value.Source is not { } source) return Result.Success(admission.Value.Receipt);

        // The admitted attempt owns a bounded lifetime independently of a disappearing HTTP client.
        // No background task, redispatch on restart, or automatic provider retry is created.
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        AudioTranscriptionProviderResult result;
        try
        {
            await using var input = await blobs.OpenReferenceReadAsync(source.ReferenceId, userId, lifetime.Token);
            if (input is null) result = new(null, "input-unavailable");
            else
            {
                using var buffer = new MemoryStream(); var bytes = new byte[65536]; int count;
                while ((count = await input.ReadAsync(bytes, lifetime.Token)) > 0)
                {
                    if (buffer.Length + count > source.ByteSize) throw new DomainException(ErrorCodes.ValidationError, "Original size mismatch.");
                    buffer.Write(bytes, 0, count);
                }
                var original = buffer.ToArray();
                if (original.LongLength != source.ByteSize || Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() != source.ContentHash)
                    result = new(null, "input-unavailable");
                else if (await killSwitch.IsKilledAsync(LlmSurface.Worker, userId, lifetime.Token)) result = new(null, "provider-unavailable");
                else if (!await store.DispatchAllowedAsync(userId, admission.Value.Receipt.Id, time.GetUtcNow(), lifetime.Token)) result = new(null, "access-changed");
                else result = await provider.TranscribeAsync(original, source.MediaType, lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { result = new(null, "provider-timeout"); }
        catch (DomainException) { result = new(null, "input-unavailable"); }
        catch (IOException) { result = new(null, "input-unavailable"); }
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        return await store.FinishAsync(userId, admission.Value.Receipt.Id, result, time.GetUtcNow(), completion.Token);
    }
}
