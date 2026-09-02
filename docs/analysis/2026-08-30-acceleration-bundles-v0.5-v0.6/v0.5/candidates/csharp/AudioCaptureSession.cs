namespace Taskdeck.AccelerationCandidates;

public enum AudioSessionState { Open, Finalizing, Finalized, Aborted }
public sealed class AudioCaptureSession
{
    private readonly SortedDictionary<int, string> _chunks = new();
    public Guid Id { get; } = Guid.NewGuid();
    public Guid OwnerUserId { get; }
    public AudioSessionState State { get; private set; } = AudioSessionState.Open;
    public long DeclaredMaxBytes { get; }
    public long ReceivedBytes { get; private set; }
    public int? FinalChunkIndex { get; private set; }

    public AudioCaptureSession(Guid ownerUserId, long declaredMaxBytes)
    {
        if (ownerUserId == Guid.Empty || declaredMaxBytes <= 0) throw new ArgumentException("Invalid session parameters.");
        OwnerUserId = ownerUserId; DeclaredMaxBytes = declaredMaxBytes;
    }

    public bool RegisterChunk(int index, long bytes, string sha256, bool final)
    {
        if (State != AudioSessionState.Open || index < 0 || bytes <= 0 || string.IsNullOrWhiteSpace(sha256)) throw new InvalidOperationException("Chunk not admissible.");
        if (_chunks.TryGetValue(index, out var existing)) return StringComparer.Ordinal.Equals(existing, sha256);
        if (ReceivedBytes + bytes > DeclaredMaxBytes) throw new InvalidOperationException("Declared byte ceiling exceeded.");
        _chunks[index] = sha256; ReceivedBytes += bytes;
        if (final) FinalChunkIndex = index;
        return true;
    }

    public void BeginFinalize()
    {
        if (FinalChunkIndex is null || Enumerable.Range(0, FinalChunkIndex.Value + 1).Any(i => !_chunks.ContainsKey(i)))
            throw new InvalidOperationException("Chunks are incomplete.");
        State = AudioSessionState.Finalizing;
    }

    public void Complete() { if (State != AudioSessionState.Finalizing) throw new InvalidOperationException(); State = AudioSessionState.Finalized; }
    public void Abort() { if (State == AudioSessionState.Finalized) throw new InvalidOperationException(); State = AudioSessionState.Aborted; }
}
