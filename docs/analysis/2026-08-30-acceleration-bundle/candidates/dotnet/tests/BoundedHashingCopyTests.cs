using System.IO;
using System.Text;
using System.Threading.Tasks;
using Taskdeck.Acceleration.Candidates.Blobs;
using Xunit;

namespace Taskdeck.Acceleration.Candidates.Tests.Blobs;

public sealed class BoundedHashingCopyTests
{
    [Fact]
    public async Task Copies_and_hashes_without_materialising_the_whole_input()
    {
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("taskdeck"));
        await using var destination = new MemoryStream();
        var result = await BoundedHashingCopy.CopyAsync(source, destination, 8, 16, bufferSize: 4096);
        Assert.Equal(8, result.BytesCopied);
        Assert.Equal("taskdeck", Encoding.UTF8.GetString(destination.ToArray()));
        Assert.Equal(64, result.Sha256Hex.Length);
    }

    [Fact]
    public async Task Reports_absolute_cap_when_one_read_crosses_both_limits()
    {
        await using var source = new MemoryStream(new byte[13]);
        await using var destination = new MemoryStream();
        var error = await Assert.ThrowsAsync<BlobSizeLimitException>(
            () => BoundedHashingCopy.CopyAsync(source, destination, 10, 12, bufferSize: 4096));
        Assert.Equal("blob_absolute_size_exceeded", error.Code);
    }
}
