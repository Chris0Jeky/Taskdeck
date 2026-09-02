
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Taskdeck.Acceleration.V06;

public sealed record ProcessingCacheInput(string Id, string Role, string ContentHash);

public sealed record ProcessingCacheKey(
    int CanonicalizationVersion,
    Guid OwnerUserId,
    string Capability,
    IReadOnlyList<ProcessingCacheInput> Inputs,
    string ProcessorId,
    string ProcessorVersion,
    string? ModelSnapshot,
    string ConfigurationDigest,
    IReadOnlyList<string> OutputSchemas,
    int ProtocolVersion)
{
    public string CanonicalJson()
    {
        var value = new
        {
            canonicalizationVersion = CanonicalizationVersion,
            ownerUserId = OwnerUserId,
            capability = Capability,
            inputs = Inputs.Select(x => new
            {
                id = x.Id,
                role = x.Role,
                contentHash = x.ContentHash.ToLowerInvariant()
            }).ToArray(),
            processorId = ProcessorId,
            processorVersion = ProcessorVersion,
            modelSnapshot = ModelSnapshot,
            configurationDigest = ConfigurationDigest,
            outputSchemas = OutputSchemas.Order(StringComparer.Ordinal).ToArray(),
            protocolVersion = ProtocolVersion
        };

        return JsonSerializer.Serialize(value);
    }

    public string Digest() =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson()))).ToLowerInvariant();
}
